namespace Ows.Core.Notarization;

/// <summary>
/// Represents the server-side receipt issued for a submitted checkpoint.
/// </summary>
public sealed record CheckpointReceipt {
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
    /// Gets the previous receipt hash in the receipt chain.
    /// </summary>
    public string PreviousReceiptHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the receipt hash returned by the verifier.
    /// </summary>
    public string ReceiptHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional server signature over the receipt hash.
    /// </summary>
    public string ServerSignature { get; init; } = string.Empty;

    /// <summary>
    /// Gets the server-issued timestamp for the receipt.
    /// </summary>
    public ServerTimestamp ServerTimestamp { get; init; } = new();

    /// <summary>
    /// Gets the fingerprint of the signing key used to sign the receipt.
    /// </summary>
    public string SigningKeyFingerprint { get; init; } = string.Empty;
}
