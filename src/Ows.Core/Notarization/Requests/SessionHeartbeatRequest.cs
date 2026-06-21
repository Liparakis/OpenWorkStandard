namespace Ows.Core.Notarization;

/// <summary>
/// Represents a session heartbeat request payload from the client.
/// </summary>
public sealed record SessionHeartbeatRequest {
    /// <summary>
    /// Gets the current local event head hash from the client.
    /// </summary>
    public string? LastKnownEventHash { get; init; }
}
