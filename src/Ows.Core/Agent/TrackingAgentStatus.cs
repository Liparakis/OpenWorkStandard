namespace Ows.Core.Agent;

/// <summary>
///     Describes the current lifecycle state of the local tracking agent.
/// </summary>
public enum TrackingAgentStatus {
    /// <summary>
    ///     The agent has not been prepared yet.
    /// </summary>
    Idle,

    /// <summary>
    ///     The agent has been configured and the initial project scan is complete,
    ///     but the continuous watch loop has not started yet.
    /// </summary>
    Ready,

    /// <summary>
    ///     The agent is actively watching for file-system changes and appending
    ///     events to the provenance timeline.
    /// </summary>
    Watching,

    /// <summary>
    ///     The agent was running and has since been stopped gracefully.
    /// </summary>
    Stopped
}
