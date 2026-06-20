using System;

namespace Ows.Core.Notarization;

/// <summary>
/// Represents a session heartbeat response payload from the verifier.
/// </summary>
public sealed record SessionHeartbeatResponse
{
    /// <summary>
    /// Gets the UTC time of the verifier server.
    /// </summary>
    public DateTimeOffset ServerTime { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the UTC timestamp when the current session lease expires.
    /// </summary>
    public DateTimeOffset LeaseExpiresAt { get; init; }

    /// <summary>
    /// Gets the simple trust or continuity status of the session.
    /// </summary>
    public string SessionTrustState { get; init; } = "Active";

    /// <summary>
    /// Gets the current authoritative session head response.
    /// </summary>
    public SessionHeadResponse SessionHead { get; init; } = new();
}
