using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Ows.Core.Education;
using Ows.Core.Notarization;
using Ows.Core.Verification;

namespace Ows.Verifier.Server;

/// <summary>
/// Runs queued package verification jobs inside the verifier process.
/// </summary>
internal sealed class PackageVerificationWorker : BackgroundService
{
    private readonly IVerifierAuditStore _auditStore;
    private readonly IPackageBlobStore _blobStore;
    private readonly IEducationStore _educationStore;
    private readonly IPackageVerificationJobStore _jobStore;
    private readonly ILogger<PackageVerificationWorker> _logger;
    private readonly IPackageVerifier _packageVerifier;
    private readonly IPackageSubmissionStore _packageStore;
    private readonly VerifierStorageOptions _options;
    private readonly IVerifierStorage _storage;

    /// <summary>
    /// Initializes a new package verification worker.
    /// </summary>
    public PackageVerificationWorker(
        IPackageVerificationJobStore jobStore,
        IPackageSubmissionStore packageStore,
        IPackageBlobStore blobStore,
        IVerifierStorage storage,
        IEducationStore educationStore,
        IPackageVerifier packageVerifier,
        IVerifierAuditStore auditStore,
        VerifierStorageOptions options,
        ILogger<PackageVerificationWorker> logger)
    {
        _jobStore = jobStore;
        _packageStore = packageStore;
        _blobStore = blobStore;
        _storage = storage;
        _educationStore = educationStore;
        _packageVerifier = packageVerifier;
        _auditStore = auditStore;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(Math.Max(50, _options.PackageWorkerPollIntervalMilliseconds));
        var staleThreshold = TimeSpan.FromSeconds(Math.Max(30, _options.PackageWorkerStaleRunningTimeoutSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await _jobStore.TryStartNextAsync(staleThreshold, stoppingToken);
                if (job is null)
                {
                    await Task.Delay(pollInterval, stoppingToken);
                    continue;
                }

                await VerifyAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Package verification worker loop failed.");
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
    }

    private async Task VerifyAsync(PackageVerificationJobRecord job, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting package verification job {JobId} for package {PackageId}.", job.Id, job.PackageId);
        await SafeAuditAsync(new VerifierAuditEvent
        {
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
        if (submission is null)
        {
            await FailAsync(job, "Package submission record not found.", cancellationToken);
            return;
        }

        if (!await _blobStore.ExistsAsync(submission.ObjectKey, cancellationToken))
        {
            await SafeAuditAsync(new VerifierAuditEvent
            {
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

        try
        {
            ReceiptChain? trustedReceiptChain = null;
            SessionHeadResponse? trustedSessionHead = null;
            VerifierSessionRecord? verifierSession = null;

            if (!string.IsNullOrWhiteSpace(submission.SessionId))
            {
                var sessionId = new AssessmentSessionId(submission.SessionId);
                try
                {
                    trustedReceiptChain = await _storage.GetReceiptsAsync(sessionId, cancellationToken);
                    trustedSessionHead = await _storage.GetHeadAsync(sessionId, cancellationToken);
                    verifierSession = await _storage.GetSessionAsync(sessionId, cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    // ponytail: keep verifying locally and let the result explain the missing remote anchor.
                }
            }

            var verificationResult = await _packageVerifier.VerifyAsync(
                new PackageVerificationRequest
                {
                    PackagePath = packagePath,
                    TrustedReceiptChain = trustedReceiptChain,
                    TrustedSessionHead = trustedSessionHead,
                    SessionLastHeartbeatAt = verifierSession?.LastHeartbeatAt,
                    SessionLeaseExpiresAt = verifierSession?.LeaseExpiresAt,
                    SessionHasLeaseGap = verifierSession?.HasLeaseGap ?? false,
                    SessionMaxLeaseGapSeconds = verifierSession?.MaxLeaseGapSeconds ?? 0,
                    SessionFirstLeaseGapAt = verifierSession?.FirstLeaseGapAt,
                    EducationContext = await ResolveEducationContextAsync(submission, cancellationToken)
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
            await SafeAuditAsync(new VerifierAuditEvent
            {
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
            await SafeAuditAsync(new VerifierAuditEvent
            {
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
        }
        catch (Exception exception)
        {
            await FailAsync(job, exception.Message, cancellationToken, submission);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    private async Task FailAsync(
        PackageVerificationJobRecord job,
        string message,
        CancellationToken cancellationToken,
        VerifierPackageSubmissionResponse? submission = null)
    {
        _logger.LogWarning("Package verification job {JobId} failed: {Message}", job.Id, message);
        await _jobStore.CompleteAsync(job.Id, "Failed", null, message, cancellationToken);
        if (submission is null)
        {
            submission = await _packageStore.GetAsync(job.PackageId, cancellationToken);
        }

        if (submission is not null)
        {
            await _packageStore.UpdateVerificationStateAsync(
                submission.SubmissionId,
                "Failed",
                job.Id,
                null,
                null,
                message,
                cancellationToken);
        }

        await SafeAuditAsync(new VerifierAuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            EventType = "package.verification.failed",
            ActorKeyId = job.RequestedByApiKeyId,
            InstitutionId = submission?.InstitutionId,
            SessionId = submission?.SessionId,
            PackageId = submission?.SubmissionId ?? job.PackageId,
            AssessmentId = submission?.AssessmentId,
            Result = "Failed",
            Metadata = new Dictionary<string, string?>
            {
                ["jobId"] = job.Id,
                ["error"] = message
            }
        }, cancellationToken);
    }

    private async Task<ReportEducationContext?> ResolveEducationContextAsync(
        VerifierPackageSubmissionResponse submission,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(submission.InstitutionId) &&
            string.IsNullOrWhiteSpace(submission.AssessmentId) &&
            string.IsNullOrWhiteSpace(submission.StudentUserId))
        {
            return null;
        }

        Institution? institution = null;
        if (!string.IsNullOrWhiteSpace(submission.InstitutionId))
        {
            institution = await _educationStore.GetInstitutionAsync(new InstitutionId(submission.InstitutionId), cancellationToken);
        }

        Assessment? assessment = null;
        Course? course = null;
        CourseOffering? offering = null;
        if (!string.IsNullOrWhiteSpace(submission.AssessmentId))
        {
            assessment = await _educationStore.GetAssessmentAsync(new AssessmentId(submission.AssessmentId), cancellationToken);
            if (assessment is not null)
            {
                offering = await _educationStore.GetCourseOfferingAsync(assessment.CourseOfferingId, cancellationToken);
                if (offering is not null)
                {
                    course = await _educationStore.GetCourseAsync(offering.CourseId, cancellationToken);
                }
            }
        }

        User? student = null;
        if (!string.IsNullOrWhiteSpace(submission.StudentUserId))
        {
            student = await _educationStore.GetUserAsync(new UserId(submission.StudentUserId), cancellationToken);
        }

        return new ReportEducationContext
        {
            InstitutionId = institution?.Id.Value,
            InstitutionName = institution?.Name,
            CourseId = course?.Id.Value,
            CourseCode = course?.Code,
            CourseTitle = course?.Title,
            ClassGroupId = offering?.ClassGroupId.Value,
            AssessmentId = assessment?.Id.Value,
            AssessmentTitle = assessment?.Title,
            StudentUserId = student?.Id.Value,
            StudentDisplayName = student?.DisplayName,
            StudentExternalId = student?.ExternalId
        };
    }

    private async Task<string> CopyToTempFileAsync(Stream source, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.owspkg");
        await using var destination = File.Create(tempPath);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        return tempPath;
    }

    private async Task SafeAuditAsync(VerifierAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        try
        {
            await _auditStore.AppendAsync(auditEvent, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to persist worker audit event {EventType}.", auditEvent.EventType);
        }
    }
}
