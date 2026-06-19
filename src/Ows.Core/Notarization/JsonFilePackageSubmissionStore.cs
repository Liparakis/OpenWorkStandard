using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ows.Core.Notarization;

/// <summary>
/// Persists package metadata registrations to a local JSON file.
/// </summary>
public sealed class JsonFilePackageSubmissionStore : IPackageSubmissionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly Lock _gate = new();
    private readonly string _storePath;
    private readonly Dictionary<string, VerifierPackageSubmissionResponse> _submissions = [];

    /// <summary>
    /// Initializes a new instance of the JsonFilePackageSubmissionStore class.
    /// </summary>
    /// <param name="storePath">The path to the JSON storage file.</param>
    public JsonFilePackageSubmissionStore(string storePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        _storePath = storePath;
        LoadFromDisk();
    }

    /// <inheritdoc />
    public Task<VerifierPackageSubmissionResponse> SubmitAsync(
        VerifierPackageSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var validationError = request.GetValidationError();
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existingByIdempotency = _submissions.Values
                    .FirstOrDefault(s => string.Equals(s.IdempotencyKey, request.IdempotencyKey, StringComparison.Ordinal));
                if (existingByIdempotency is not null)
                {
                    if (!MatchesRequest(existingByIdempotency, request))
                    {
                        throw new InvalidOperationException("Package idempotency key already exists with different metadata.");
                    }
                    return Task.FromResult(existingByIdempotency);
                }
            }

            var existingByLocation = _submissions.Values
                .FirstOrDefault(s => string.Equals(s.ObjectStorageProvider, request.ObjectStorageProvider, StringComparison.Ordinal)
                                  && string.Equals(s.ObjectBucket, request.ObjectBucket, StringComparison.Ordinal)
                                  && string.Equals(s.ObjectKey, request.ObjectKey, StringComparison.Ordinal));
            if (existingByLocation is not null)
            {
                if (!MatchesRequest(existingByLocation, request))
                {
                    throw new InvalidOperationException("Package object is already registered with different metadata.");
                }
                return Task.FromResult(existingByLocation);
            }

            var submissionId = Guid.NewGuid().ToString("N");
            var response = new VerifierPackageSubmissionResponse
            {
                SubmissionId = submissionId,
                SessionId = request.SessionId,
                IdempotencyKey = request.IdempotencyKey,
                ObjectStorageProvider = request.ObjectStorageProvider,
                ObjectBucket = request.ObjectBucket,
                ObjectKey = request.ObjectKey,
                PackageSha256 = request.PackageSha256.ToLowerInvariant(),
                PackageSizeBytes = request.PackageSizeBytes,
                VerificationStatus = "Registered",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            _submissions.Add(submissionId, response);
            SaveToDisk();
            return Task.FromResult(response);
        }
    }

    /// <inheritdoc />
    public Task<VerifierPackageSubmissionResponse?> GetAsync(
        string submissionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(submissionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _submissions.TryGetValue(submissionId, out var submission);
            return Task.FromResult(submission);
        }
    }

    /// <inheritdoc />
    public Task UpdateVerificationResultAsync(
        string submissionId,
        string verificationStatus,
        string trustStatus,
        string verificationResultJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(submissionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_submissions.TryGetValue(submissionId, out var existing))
            {
                throw new InvalidOperationException($"Submission not found: {submissionId}");
            }

            var updated = existing with
            {
                VerificationStatus = verificationStatus,
                TrustStatus = trustStatus,
                VerificationResultJson = verificationResultJson
            };

            _submissions[submissionId] = updated;
            SaveToDisk();
            return Task.CompletedTask;
        }
    }

    private static bool MatchesRequest(VerifierPackageSubmissionResponse existing, VerifierPackageSubmissionRequest request) =>
        string.Equals(existing.SessionId, request.SessionId, StringComparison.Ordinal) &&
        string.Equals(existing.ObjectStorageProvider, request.ObjectStorageProvider, StringComparison.Ordinal) &&
        string.Equals(existing.ObjectBucket, request.ObjectBucket, StringComparison.Ordinal) &&
        string.Equals(existing.ObjectKey, request.ObjectKey, StringComparison.Ordinal) &&
        string.Equals(existing.PackageSha256, request.PackageSha256, StringComparison.OrdinalIgnoreCase) &&
        existing.PackageSizeBytes == request.PackageSizeBytes;

    private void LoadFromDisk()
    {
        if (!File.Exists(_storePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_storePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var snapshot = JsonSerializer.Deserialize<List<VerifierPackageSubmissionResponse>>(json, SerializerOptions);
            if (snapshot is not null)
            {
                foreach (var submission in snapshot)
                {
                    _submissions[submission.SubmissionId] = submission;
                }
            }
        }
        catch
        {
            // Fail silently or start empty if deserialization fails
        }
    }

    private void SaveToDisk()
    {
        var dir = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var json = JsonSerializer.Serialize(_submissions.Values.ToList(), SerializerOptions);
        var temporaryPath = $"{_storePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _storePath, true);
    }
}
