using Ows.Core.Notarization;

namespace Ows.Cli;

/// <summary>
/// Implements the receipt transport contract using local session-store persistence only.
/// </summary>
public sealed class LocalReceiptTransport(string projectRoot) : IReceiptTransport
{
    /// <inheritdoc />
    public Task<AssessmentSessionId> StartSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OwsSessionStore.StartSession(projectRoot));
    }

    /// <inheritdoc />
    public Task<CheckpointReceipt> SendCheckpointAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OwsSessionStore.AddCheckpoint(projectRoot));
    }

    /// <inheritdoc />
    public Task<ReceiptChain> GetReceiptsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OwsSessionStore.GetReceipts(projectRoot));
    }
}
