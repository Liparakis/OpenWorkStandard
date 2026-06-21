namespace Ows.Core.Notarization;

/// <summary>
/// Represents package metadata registered after the package blob is stored in object storage.
/// </summary>
public sealed record VerifierPackageSubmissionRequest {
    /// <summary>
    /// Gets the optional verifier session identifier associated with the package.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the optional institution identifier.
    /// </summary>
    public string? InstitutionId { get; init; }

    /// <summary>
    /// Gets the optional assessment identifier.
    /// </summary>
    public string? AssessmentId { get; init; }

    /// <summary>
    /// Gets the optional student user identifier.
    /// </summary>
    public string? StudentUserId { get; init; }

    /// <summary>
    /// Gets the optional idempotency key supplied by the caller.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Gets the object storage provider name.
    /// </summary>
    public string ObjectStorageProvider { get; init; } = string.Empty;

    /// <summary>
    /// Gets the object storage bucket or container name.
    /// </summary>
    public string ObjectBucket { get; init; } = string.Empty;

    /// <summary>
    /// Gets the object storage key for the package blob.
    /// </summary>
    public string ObjectKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets the SHA-256 hash of the package blob.
    /// </summary>
    public string PackageSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the package size in bytes.
    /// </summary>
    public long PackageSizeBytes { get; init; }

    /// <summary>
    /// Returns the first validation error for the package submission request.
    /// </summary>
    /// <returns>The validation error, or <see langword="null"/> when valid.</returns>
    public string? GetValidationError() {
        if (IdempotencyKey is not null && string.IsNullOrWhiteSpace(IdempotencyKey)) {
            return "Idempotency-Key header must not be empty when provided.";
        }

        if (!string.IsNullOrWhiteSpace(SessionId) && SessionId.Length > 200) {
            return "Session id is too long.";
        }

        if (string.IsNullOrWhiteSpace(ObjectStorageProvider)) {
            return "Object storage provider is required.";
        }

        if (string.IsNullOrWhiteSpace(ObjectBucket)) {
            return "Object bucket is required.";
        }

        if (string.IsNullOrWhiteSpace(ObjectKey)) {
            return "Object key is required.";
        }

        if (PackageSha256.Length != 64 || PackageSha256.Any(static c => !Uri.IsHexDigit(c))) {
            return "Package SHA-256 must be a 64-character hex string.";
        }

        if (PackageSizeBytes <= 0) {
            return "Package size must be greater than zero.";
        }

        return null;
    }
}
