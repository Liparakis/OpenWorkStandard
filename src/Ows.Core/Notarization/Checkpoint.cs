namespace Ows.Core.Notarization;

/// <summary>
/// Represents a client-side checkpoint submitted for notarization.
/// </summary>
public sealed record Checkpoint
{
    /// <summary>
    /// Gets the session identifier associated with the checkpoint.
    /// </summary>
    public AssessmentSessionId SessionId { get; init; }

    /// <summary>
    /// Gets the checkpoint sequence number within the session.
    /// </summary>
    public int SequenceNumber { get; init; }

    /// <summary>
    /// Gets the local timeline head hash observed at the checkpoint.
    /// </summary>
    public string TimelineHeadHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC time when the client created the checkpoint.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
