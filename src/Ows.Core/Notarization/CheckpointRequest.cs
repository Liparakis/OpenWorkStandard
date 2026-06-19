namespace Ows.Core.Notarization;

/// <summary>
/// Represents the minimal request body for submitting a checkpoint to the verifier.
/// </summary>
public sealed record CheckpointRequest
{
    /// <summary>
    /// Gets the session identifier associated with the checkpoint.
    /// </summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the checkpoint sequence number within the session.
    /// </summary>
    public int SequenceNumber { get; init; }

    /// <summary>
    /// Gets the timeline head hash being notarized.
    /// </summary>
    public string TimelineHeadHash { get; init; } = string.Empty;
}
