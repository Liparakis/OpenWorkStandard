namespace Ows.Agent;

/// <summary>
/// Defines the local tracking agent contract used by the CLI.
/// </summary>
public interface ITrackingAgent
{
    /// <summary>
    /// Gets the current tracking agent status.
    /// </summary>
    TrackingAgentStatus Status { get; }

    /// <summary>
    /// Prepares the agent skeleton for a tracked project.
    /// </summary>
    /// <param name="options">The tracking options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result describing the current placeholder state.</returns>
    Task<TrackingAgentOperationResult> PrepareAsync(TrackingAgentOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Attempts to start active tracking.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result describing the current placeholder state.</returns>
    Task<TrackingAgentOperationResult> StartAsync(CancellationToken cancellationToken);
}
