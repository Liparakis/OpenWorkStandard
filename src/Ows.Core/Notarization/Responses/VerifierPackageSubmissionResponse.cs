namespace Ows.Core.Notarization;

/// <summary>
/// Represents a registered package object and its verifier metadata.
/// </summary>
public sealed record VerifierPackageSubmissionResponse
{
    /// <summary>
    /// Gets the durable package submission identifier.
    /// </summary>
    public string SubmissionId { get; init; } = string.Empty;

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
    /// Gets the optional idempotency key used to register the package.
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
    /// Gets the session receipt head captured when the package was registered.
    /// </summary>
    public string? SessionHeadReceiptHash { get; init; }

    /// <summary>
    /// Gets the session event head captured when the package was registered.
    /// </summary>
    public string? SessionHeadEventHash { get; init; }

    /// <summary>
    /// Gets the session checkpoint count captured when the package was registered.
    /// </summary>
    public int? SessionCheckpointCount { get; init; }

    /// <summary>
    /// Gets the current server-side package verification status.
    /// </summary>
    public string VerificationStatus { get; init; } = "Registered";

    /// <summary>
    /// Gets the optional current verification job identifier.
    /// </summary>
    public string? VerificationJobId { get; init; }

    /// <summary>
    /// Gets the trust status of the verification (e.g. Verified, Degraded, Unverified, Invalid).
    /// </summary>
    public string? TrustStatus { get; init; }

    /// <summary>
    /// Gets the full verification result JSON.
    /// </summary>
    public string? VerificationResultJson { get; init; }

    /// <summary>
    /// Gets the last verification error message, if any.
    /// </summary>
    public string? LastVerificationError { get; init; }

    /// <summary>
    /// Gets the UTC creation time for the package submission record.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; }
}
