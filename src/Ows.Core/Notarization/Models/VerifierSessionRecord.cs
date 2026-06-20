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

    /// <summary>
    /// Gets the timestamp of the last recorded session heartbeat or checkpoint in UTC.
    /// </summary>
    public DateTimeOffset? LastHeartbeatAt { get; init; }

    /// <summary>
    /// Gets the timestamp when the current session lease expires in UTC.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAt { get; init; }

    /// <summary>
    /// Gets the last known event head hash reported by the client.
    /// </summary>
    public string? LastKnownEventHash { get; init; }

    /// <summary>
    /// Gets a value indicating whether a lease expiration gap occurred during this session.
    /// </summary>
    public bool HasLeaseGap { get; init; }

    /// <summary>
    /// Gets the maximum lease expiration gap duration observed during this session, in seconds.
    /// </summary>
    public int MaxLeaseGapSeconds { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when the first lease gap started.
    /// </summary>
    public DateTimeOffset? FirstLeaseGapAt { get; init; }
}