namespace Ows.Core.Notarization;

/// <summary>
/// Represents the current authoritative receipt anchor for an assessment session.
/// </summary>
public sealed record SessionHeadResponse
{
    /// <summary>
    /// Gets the assessment session identifier.
    /// </summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the last issued receipt sequence number, or 0 when no receipts exist.
    /// </summary>
    public int LastSequenceNumber { get; init; }

    /// <summary>
    /// Gets the last acknowledged timeline head hash, or the genesis value when no receipts exist.
    /// </summary>
    public string LastTimelineHeadHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the last receipt hash, or the genesis value when no receipts exist.
    /// </summary>
    public string LastReceiptHash { get; init; } = string.Empty;
}