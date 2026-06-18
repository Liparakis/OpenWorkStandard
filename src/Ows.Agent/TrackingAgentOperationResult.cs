namespace Ows.Agent;

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

    /// <summary>
    /// Gets the descriptive message for the operation outcome.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}
