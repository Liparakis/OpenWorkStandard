namespace Ows.Core.Notarization;

/// <summary>
/// Represents durable verifier session state independent of the HTTP server process.
/// </summary>
public sealed record VerifierSessionRecord
{
    /// <summary>
    /// Gets the verifier session identifier.
    /// </summary>
    public AssessmentSessionId Id { get; init; }

    /// <summary>
    /// Gets the session creation time in UTC.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the current session head receipt hash.
    /// </summary>
    public string HeadReceiptHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the current session head event hash.
    /// </summary>
    public string HeadEventHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the committed checkpoint count for the session.
    /// </summary>
    public int CheckpointCount { get; init; }

    /// <summary>
    /// Gets the optional client identifier.
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Gets the optional assessment identifier.
    /// </summary>
    public string? AssessmentId { get; init; }

    /// <summary>
    /// Gets the optional metadata payload serialized as JSON.
    /// </summary>
    public string MetadataJson { get; init; } = "{}";
}