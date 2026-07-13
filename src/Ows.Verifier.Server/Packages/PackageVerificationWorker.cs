using System.Text.Json;
using Ows.Core.Notarization;
using Ows.Core.Verification;

namespace Ows.Verifier.Server.Packages;

/// <summary>
/// Runs queued package verification jobs inside the verifier process.
/// </summary>
internal sealed class PackageVerificationWorker : BackgroundService {
    /// <summary>
    /// The audit store used to record verification events.
    /// </summary>
    private readonly IVerifierAuditStore _auditStore;

    /// <summary>
    /// The blob store used to read uploaded packages.
    /// </summary>
    private readonly IPackageBlobStore _blobStore;

    /// <summary>
    /// The job store used to manage the verification queue status.
    /// </summary>
    private readonly IPackageVerificationJobStore _jobStore;

    /// <summary>
    /// The logger instance.
    /// </summary>
    private readonly ILogger<PackageVerificationWorker> _logger;

    /// <summary>
    /// The core package verifier engine.
    /// </summary>
    private readonly IPackageVerifier _packageVerifier;

    /// <summary>
    /// The package submission record store.
    /// </summary>
    private readonly IPackageSubmissionStore _packageStore;

    /// <summary>
    /// The storage configuration options.
    /// </summary>
    private readonly VerifierStorageOptions _options;

    /// <summary>
    /// The verifier storage backend.
    /// </summary>
    private readonly IVerifierStorage _storage;

    /// <summary>
    /// Initializes a new package verification worker.
    /// </summary>
    /// <param name="jobStore">The queue store for tracking jobs.</param>
    /// <param name="packageStore">The store holding submission metadata.</param>
    /// <param name="blobStore">The blob storage for package files.</param>
    /// <param name="storage">The verifier storage backend.</param>
    /// <param name="packageVerifier">The core verification service.</param>
    /// <param name="auditStore">The verifier audit trail store.</param>
    /// <param name="options">The storage and worker configuration options.</param>
    /// <param name="logger">The logger instance.</param>
    public PackageVerificationWorker(
        IPackageVerificationJobStore jobStore,
        IPackageSubmissionStore packageStore,
        IPackageBlobStore blobStore,
        IVerifierStorage storage,
        IPackageVerifier packageVerifier,
        IVerifierAuditStore auditStore,
        VerifierStorageOptions options,
        ILogger<PackageVerificationWorker> logger) {
        _jobStore = jobStore;
        _packageStore = packageStore;
        _blobStore = blobStore;
        _storage = storage;
        _packageVerifier = packageVerifier;
        _auditStore = auditStore;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var pollInterval = TimeSpan.FromMilliseconds(Math.Max(50, _options.PackageWorkerPollIntervalMilliseconds));
        var staleThreshold = TimeSpan.FromSeconds(Math.Max(30, _options.PackageWorkerStaleRunningTimeoutSeconds));

        while (!stoppingToken.IsCancellationRequested) {
            try {
                var job = await _jobStore.TryStartNextAsync(staleThreshold, stoppingToken);
                if (job is null) {
                    await Task.Delay(pollInterval, stoppingToken);
                    continue;
                }

                await VerifyAsync(job, stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception exception) {
                _logger.LogError(exception, "Package verification worker loop failed.");
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Executes the package verification process for a single job.
    /// </summary>
    /// <param name="job">The job record details.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous verification operation.</returns>
    private async Task VerifyAsync(PackageVerificationJobRecord job, CancellationToken cancellationToken) {
        _logger.LogInformation("Starting package verification job {JobId} for package {PackageId}.", job.Id,
            job.PackageId);
        await SafeAuditAsync(new VerifierAuditEvent {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            EventType = "package.verification.started",
            ActorKeyId = job.RequestedByApiKeyId,
            PackageId = job.PackageId,
            Result = "Running",
            Metadata = new Dictionary<string, string?> { ["jobId"] = job.Id }
        }, cancellationToken);

        await _packageStore.UpdateVerificationStateAsync(
            job.PackageId,
            "Running",
            job.Id,
            null,
            null,
            null,
            cancellationToken);

        var submission = await _packageStore.GetAsync(job.PackageId, cancellationToken);
        if (submission is null) {
            await FailAsync(job, "Package submission record not found.", cancellationToken);
            return;
        }

        if (!await _blobStore.ExistsAsync(submission.ObjectKey, cancellationToken)) {
            await SafeAuditAsync(new VerifierAuditEvent {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                EventType = "package.blob.missing",
                ActorKeyId = job.RequestedByApiKeyId,
                InstitutionId = submission.InstitutionId,
                SessionId = submission.SessionId,
                PackageId = submission.SubmissionId,
                AssessmentId = submission.AssessmentId,
                Result = "Missing"
            }, cancellationToken);
            await FailAsync(job, "Package blob is missing from local package storage.", cancellationToken, submission);
            return;
        }

        await using var packageStream = await _blobStore.OpenReadAsync(submission.ObjectKey, cancellationToken);
        var packagePath = await CopyToTempFileAsync(packageStream, cancellationToken);

        try {
            ReceiptChain? trustedReceiptChain = null;
            SessionHeadResponse? trustedSessionHead = null;
            VerifierSessionRecord? verifierSession = null;

            if (!string.IsNullOrWhiteSpace(submission.SessionId)) {
                var sessionId = new AssessmentSessionId(submission.SessionId);
                try {
                    trustedReceiptChain = await _storage.GetReceiptsAsync(sessionId, cancellationToken);
                    trustedSessionHead = await _storage.GetHeadAsync(sessionId, cancellationToken);
                    verifierSession = await _storage.GetSessionAsync(sessionId, cancellationToken);
                } catch (InvalidOperationException) {
                    // ponytail: keep verifying locally and let the result explain the missing remote anchor.
                }
            }

            var verificationResult = await _packageVerifier.VerifyAsync(
                new PackageVerificationRequest {
                    PackagePath = packagePath,
                    TrustedReceiptChain = trustedReceiptChain,
                    TrustedSessionHead = trustedSessionHead,
                    SessionLastHeartbeatAt = verifierSession?.LastHeartbeatAt,
                    SessionLeaseExpiresAt = verifierSession?.LeaseExpiresAt,
                    SessionHasLeaseGap = verifierSession?.HasLeaseGap ?? false,
                    SessionMaxLeaseGapSeconds = verifierSession?.MaxLeaseGapSeconds ?? 0,
                    SessionFirstLeaseGapAt = verifierSession?.FirstLeaseGapAt,
                    ExternalContext = BuildExternalContext(submission)
                },
                cancellationToken);

            var resultJson = JsonSerializer.Serialize(verificationResult);
            var status = verificationResult.IsSuccess ? "Succeeded" : "Failed";
            await _jobStore.CompleteAsync(job.Id, status, resultJson, null, cancellationToken);
            await _packageStore.UpdateVerificationStateAsync(
                submission.SubmissionId,
                "Completed",
                job.Id,
                verificationResult.TrustStatus.ToString(),
                resultJson,
                null,
                cancellationToken);
            await SafeAuditAsync(new VerifierAuditEvent {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                EventType = "package.verification.completed",
                ActorKeyId = job.RequestedByApiKeyId,
                InstitutionId = submission.InstitutionId,
                SessionId = submission.SessionId,
                PackageId = submission.SubmissionId,
                AssessmentId = submission.AssessmentId,
                Result = verificationResult.TrustStatus.ToString(),
                Metadata = new Dictionary<string, string?> { ["jobId"] = job.Id }
            }, cancellationToken);
            await SafeAuditAsync(new VerifierAuditEvent {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                EventType = "package.verified",
                ActorKeyId = job.RequestedByApiKeyId,
                InstitutionId = submission.InstitutionId,
                SessionId = submission.SessionId,
                PackageId = submission.SubmissionId,
                AssessmentId = submission.AssessmentId,
                Result = verificationResult.TrustStatus.ToString(),
                Metadata = new Dictionary<string, string?> { ["jobId"] = job.Id }
            }, cancellationToken);
        } catch (Exception exception) {
            await FailAsync(job, exception.Message, cancellationToken, submission);
        } finally {
            File.Delete(packagePath);
        }
    }

    /// <summary>
    /// Marks a job as failed and updates the associated submission status and audit trail.
    /// </summary>
    /// <param name="job">The job record.</param>
    /// <param name="message">The failure reason message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="submission">The optional associated package submission response.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task FailAsync(
        PackageVerificationJobRecord job,
        string message,
        CancellationToken cancellationToken,
        VerifierPackageSubmissionResponse? submission = null) {
        _logger.LogWarning("Package verification job {JobId} failed: {Message}", job.Id, message);
        await _jobStore.CompleteAsync(job.Id, "Failed", null, message, cancellationToken);
        submission ??= await _packageStore.GetAsync(job.PackageId, cancellationToken);

        if (submission is not null) {
            await _packageStore.UpdateVerificationStateAsync(
                submission.SubmissionId,
                "Failed",
                job.Id,
                null,
                null,
                message,
                cancellationToken);
        }

        await SafeAuditAsync(new VerifierAuditEvent {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            EventType = "package.verification.failed",
            ActorKeyId = job.RequestedByApiKeyId,
            InstitutionId = submission?.InstitutionId,
            SessionId = submission?.SessionId,
            PackageId = submission?.SubmissionId ?? job.PackageId,
            AssessmentId = submission?.AssessmentId,
            Result = "Failed",
            Metadata = new Dictionary<string, string?> {
                ["jobId"] = job.Id,
                ["error"] = message
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Builds an opaque external context report for verification.
    /// </summary>
    /// <param name="submission">The package submission details.</param>
    /// <returns>The external context report, or null if no relevant ids exist.</returns>
    private static ReportExternalContext? BuildExternalContext(VerifierPackageSubmissionResponse submission) {
        if (string.IsNullOrWhiteSpace(submission.InstitutionId) &&
            string.IsNullOrWhiteSpace(submission.AssessmentId) &&
            string.IsNullOrWhiteSpace(submission.StudentUserId)) {
            return null;
        }

        return new ReportExternalContext {
            InstitutionId = submission.InstitutionId,
            AssessmentId = submission.AssessmentId,
            StudentUserId = submission.StudentUserId
        };
    }

    /// <summary>
    /// Copies a package stream to a temporary local file on the filesystem.
    /// </summary>
    /// <param name="source">The source package blob stream.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The absolute path of the created temporary file.</returns>
    private static async Task<string> CopyToTempFileAsync(Stream source, CancellationToken cancellationToken) {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.owspkg");
        await using var destination = File.Create(tempPath);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        return tempPath;
    }

    /// <summary>
    /// Appends an audit event to the audit store, capturing and logging any exceptions that occur.
    /// </summary>
    /// <param name="auditEvent">The audit event to persist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task SafeAuditAsync(VerifierAuditEvent auditEvent, CancellationToken cancellationToken) {
        try {
            await _auditStore.AppendAsync(auditEvent, cancellationToken);
        } catch (Exception exception) {
            _logger.LogWarning(exception, "Failed to persist worker audit event {EventType}.", auditEvent.EventType);
        }
    }
}
