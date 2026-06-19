namespace Ows.Core.Notarization;

/// <summary>
/// Represents the ordered receipts issued for an assessment session.
/// </summary>
public sealed record ReceiptChain
{
    /// <summary>
    /// Gets the session identifier shared by the receipt chain.
    /// </summary>
    public AssessmentSessionId SessionId { get; init; }

    /// <summary>
    /// Gets the ordered receipts currently known for the session.
    /// </summary>
    public IReadOnlyList<CheckpointReceipt> Receipts { get; init; } = [];
}