namespace Ows.Core.Notarization;

/// <summary>
/// Represents the server-side receipt issued for a submitted checkpoint.
/// </summary>
public sealed record CheckpointReceipt
{
    /// <summary>
    /// Gets the session identifier associated with the receipt.
    /// </summary>
    public AssessmentSessionId SessionId { get; init; }

    /// <summary>
    /// Gets the checkpoint sequence number covered by the receipt.
    /// </summary>
    public int SequenceNumber { get; init; }

    /// <summary>
    /// Gets the timeline head hash acknowledged by the server.
    /// </summary>
    public string TimelineHeadHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the receipt hash returned by the verifier.
    /// </summary>
    public string ReceiptHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the server-issued timestamp for the receipt.
    /// </summary>
    public ServerTimestamp ServerTimestamp { get; init; } = new();
}
