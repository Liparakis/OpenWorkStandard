using Ows.Core.Events;

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
    /// Gets the optional request idempotency key used for safe client retries.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// Gets the UTC time when the client created the checkpoint.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a checkpoint from the current head of a local timeline.
    /// </summary>
    /// <param name="timelinePath">The path to the local timeline file.</param>
    /// <param name="sessionId">The assessment session identifier.</param>
    /// <param name="sequenceNumber">The checkpoint sequence number within the session.</param>
    /// <returns>A checkpoint anchored to the current timeline head hash.</returns>
    public static Checkpoint FromTimeline(string timelinePath, AssessmentSessionId sessionId, int sequenceNumber) =>
        new()
        {
            SessionId = sessionId,
            SequenceNumber = sequenceNumber,
            TimelineHeadHash = OwsEventChain.ReadLastEventHash(timelinePath)
        };
}