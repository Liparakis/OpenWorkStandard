namespace Ows.Core.Agent;

/// <summary>
/// Represents the outcome of a tracking agent operation.
/// </summary>
public sealed record TrackingAgentOperationResult
{
    /// <summary>
    /// Gets a value indicating whether the requested operation completed.
    /// </summary>
    public bool Succeeded { get; init; }

    /// <summary>
    /// Gets the agent status after the operation.
    /// </summary>
    public TrackingAgentStatus Status { get; init; }
}
