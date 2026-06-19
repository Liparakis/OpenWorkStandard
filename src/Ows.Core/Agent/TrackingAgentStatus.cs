namespace Ows.Core.Agent;

/// <summary>
/// Describes the current lifecycle state of the local tracking agent skeleton.
/// </summary>
public enum TrackingAgentStatus
{
    /// <summary>
    /// The agent has not been started.
    /// </summary>
    Idle,

    /// <summary>
    /// The agent has been configured but full tracking is not implemented yet.
    /// </summary>
    Ready,

    /// <summary>
    /// A start request was made, but active tracking remains unimplemented.
    /// </summary>
    NotImplemented
}