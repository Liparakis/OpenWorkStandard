namespace Ows.Core.Notarization;

/// <summary>
/// Implements the receipt transport contract against the in-memory notarization service.
/// </summary>
public sealed class InMemoryRemoteReceiptTransport(
    InMemoryReceiptService receiptService,
    Func<Checkpoint>? checkpointFactory = null) : IReceiptTransport
{
    private AssessmentSessionId? _activeSessionId;

    /// <summary>
    /// Starts a new assessment session through the underlying receipt service.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The started session identifier.</returns>
    public Task<AssessmentSessionId> StartSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _activeSessionId = receiptService.StartSession();
        return Task.FromResult(_activeSessionId.Value);
    }

    /// <summary>
    /// Sends the next checkpoint for the current session and returns the issued receipt.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The issued checkpoint receipt.</returns>
    public Task<CheckpointReceipt> SendCheckpointAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_activeSessionId is null)
        {
            throw new InvalidOperationException("No active assessment session. Start a session first.");
        }

        var currentReceiptCount = receiptService.GetReceiptChain(_activeSessionId.Value).Receipts.Count;
        var checkpoint = checkpointFactory?.Invoke() ?? new Checkpoint();
        var normalizedCheckpoint = checkpoint with
        {
            SessionId = _activeSessionId.Value,
            SequenceNumber = currentReceiptCount + 1
        };

        return Task.FromResult(receiptService.SubmitCheckpoint(normalizedCheckpoint));
    }

    /// <summary>
    /// Gets the current receipt chain for the active session.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active session receipt chain.</returns>
    public Task<ReceiptChain> GetReceiptsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_activeSessionId is null)
        {
            throw new InvalidOperationException("No active assessment session. Start a session first.");
        }

        return Task.FromResult(receiptService.GetReceiptChain(_activeSessionId.Value));
    }
}