namespace Ows.Core.Notarization;

/// <summary>
/// Defines the minimal receipt transport contract used by OWS clients.
/// </summary>
public interface IReceiptTransport
{
    /// <summary>
    /// Starts a new assessment session.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The started assessment session identifier.</returns>
    Task<AssessmentSessionId> StartSessionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sends the next checkpoint for the active session.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The issued checkpoint receipt.</returns>
    Task<CheckpointReceipt> SendCheckpointAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current receipt chain for the active session.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current receipt chain.</returns>
    Task<ReceiptChain> GetReceiptsAsync(CancellationToken cancellationToken);
}
