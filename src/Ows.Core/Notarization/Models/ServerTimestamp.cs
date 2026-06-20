namespace Ows.Core.Notarization;

/// <summary>
/// Represents the server-issued timestamp attached to a checkpoint receipt.
/// </summary>
public sealed record ServerTimestamp
{
    /// <summary>
    /// Gets the UTC time when the server issued the receipt.
    /// </summary>
    public DateTimeOffset IssuedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}