using System.Collections.Concurrent;

namespace Ows.Core.Notarization;

/// <summary>
/// Provides a minimal in-memory receipt issuance flow for future CLI and server integration.
/// </summary>
public sealed class InMemoryReceiptService
{
    private readonly ConcurrentDictionary<AssessmentSessionId, List<CheckpointReceipt>> receiptChains = new();

    /// <summary>
    /// Starts a new assessment session.
    /// </summary>
    /// <returns>The new session identifier.</returns>
    public AssessmentSessionId StartSession()
    {
        var sessionId = AssessmentSessionId.Create();
        if (!receiptChains.TryAdd(sessionId, []))
        {
            throw new InvalidOperationException("Failed to create a unique assessment session.");
        }

        return sessionId;
    }

    /// <summary>
    /// Submits a checkpoint and issues the next receipt in the session chain.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to receipt.</param>
    /// <returns>The issued chained receipt.</returns>
    public CheckpointReceipt SubmitCheckpoint(Checkpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (!receiptChains.TryGetValue(checkpoint.SessionId, out var receipts))
        {
            throw new InvalidOperationException($"Unknown assessment session: {checkpoint.SessionId}");
        }

        lock (receipts)
        {
            var expectedSequenceNumber = receipts.Count + 1;
            if (checkpoint.SequenceNumber != expectedSequenceNumber)
            {
                throw new InvalidOperationException(
                    $"Checkpoint sequence {checkpoint.SequenceNumber} is invalid for session {checkpoint.SessionId}. Expected {expectedSequenceNumber}.");
            }

            var previousReceiptHash = receipts.Count == 0
                ? ReceiptChainVerifier.GenesisPreviousReceiptHash
                : receipts[^1].ReceiptHash;
            var receipt = ReceiptChainVerifier.IssueReceipt(checkpoint, previousReceiptHash);
            receipts.Add(receipt);
            return receipt;
        }
    }

    /// <summary>
    /// Gets the current receipt chain for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>The ordered receipt chain.</returns>
    public ReceiptChain GetReceiptChain(AssessmentSessionId sessionId)
    {
        if (!receiptChains.TryGetValue(sessionId, out var receipts))
        {
            throw new InvalidOperationException($"Unknown assessment session: {sessionId}");
        }

        lock (receipts)
        {
            return new ReceiptChain
            {
                SessionId = sessionId,
                Receipts = [.. receipts]
            };
        }
    }
}
