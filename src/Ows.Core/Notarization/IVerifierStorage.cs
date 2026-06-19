namespace Ows.Core.Notarization;

/// <summary>
/// Defines the durable storage boundary for verifier sessions, checkpoints, and receipt heads.
/// </summary>
public interface IVerifierStorage
{
    /// <summary>
    /// Creates and persists a new verifier session.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The persisted verifier session record.</returns>
    Task<VerifierSessionRecord> CreateSessionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the persisted verifier session record.
    /// </summary>
    /// <param name="sessionId">The verifier session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The persisted verifier session record.</returns>
    Task<VerifierSessionRecord> GetSessionAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Appends a checkpoint atomically and returns the committed receipt.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to append.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The committed receipt.</returns>
    Task<CheckpointReceipt> AppendCheckpointAsync(Checkpoint checkpoint, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the persisted full receipt chain for a session.
    /// </summary>
    /// <param name="sessionId">The verifier session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The persisted receipt chain.</returns>
    Task<ReceiptChain> GetReceiptsAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the persisted current receipt head for a session.
    /// </summary>
    /// <param name="sessionId">The verifier session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The persisted session head.</returns>
    Task<SessionHeadResponse> GetHeadAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Records a session heartbeat, updating lease and checking for continuity gaps.
    /// </summary>
    /// <param name="sessionId">The verifier session identifier.</param>
    /// <param name="lastKnownEventHash">The last known event head hash reported by the client, when available.</param>
    /// <param name="leaseDuration">The duration of the lease extension.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated verifier session record.</returns>
    Task<VerifierSessionRecord> RecordHeartbeatAsync(
        AssessmentSessionId sessionId,
        string? lastKnownEventHash,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Eagerly initializes storage resources (e.g. running migrations or loading snapshot).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when initialization is ready.</returns>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks the health of the storage backend.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task returning true if backend is healthy, otherwise false.</returns>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken);
}