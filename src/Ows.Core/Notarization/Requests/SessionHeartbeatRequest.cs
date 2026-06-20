using System;

namespace Ows.Core.Notarization;

/// <summary>
/// Represents a session heartbeat request payload from the client.
/// </summary>
public sealed record SessionHeartbeatRequest
{
    /// <summary>
    /// Gets the current local event head hash from the client.
    /// </summary>
    public string? LastKnownEventHash { get; init; }

    /// <summary>
    /// Gets the optional client-side recording timestamp in UTC.
    /// </summary>
    public DateTimeOffset? ClientTimestamp { get; init; }

    /// <summary>
    /// Gets the optional client status summary (e.g. "Active", "Idle").
    /// </summary>
    public string? ClientStatusSummary { get; init; }
}
