using Ows.Core.Notarization;

namespace Ows.Cli;

/// <summary>
/// Implements the receipt transport contract using local session-store persistence only.
/// </summary>
public sealed class LocalReceiptTransport(string projectRoot) : IReceiptTransport
{
    /// <inheritdoc />
    public Task<AssessmentSessionId> StartSessionAsync(CancellationToken cancellationToken) =>
        OwsSessionStore.StartSessionAsync(projectRoot, verifierUrl: null, cancellationToken);

    /// <inheritdoc />
    public Task<CheckpointReceipt> SendCheckpointAsync(CancellationToken cancellationToken) =>
        OwsSessionStore.AddCheckpointAsync(projectRoot, cancellationToken);

    /// <inheritdoc />
    public Task<ReceiptChain> GetReceiptsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OwsSessionStore.GetReceipts(projectRoot));
    }
}
