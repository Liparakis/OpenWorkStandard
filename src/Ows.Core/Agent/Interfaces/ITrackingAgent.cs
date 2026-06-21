namespace Ows.Core.Agent;

/// <summary>
/// Defines the local tracking agent contract used by the CLI and future host integrations.
/// </summary>
public interface ITrackingAgent {
    /// <summary>
    /// Gets the current tracking agent lifecycle state.
    /// </summary>
    TrackingAgentStatus Status { get; }

    /// <summary>
    /// Prepares the agent for the tracked project at the path specified in <paramref name="options"/>.
    /// Must be called before <see cref="StartAsync"/>.
    /// </summary>
    /// <param name="options">The tracking options, including the project root path and watcher settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result describing the preparation outcome.</returns>
    Task<TrackingAgentOperationResult> PrepareAsync(TrackingAgentOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Performs an initial project scan and then continuously watches for file-system changes,
    /// appending chained provenance events to the timeline until <paramref name="cancellationToken"/>
    /// is cancelled.
    /// </summary>
    /// <remarks>
    /// This method blocks until the <paramref name="cancellationToken"/> is cancelled. Callers
    /// should pass a process-lifetime token (e.g. one bound to SIGINT / Ctrl+C) so that the
    /// watcher stops gracefully when the user interrupts the process.
    /// </remarks>
    /// <param name="cancellationToken">
    /// Token that, when cancelled, causes the watcher to flush any pending debounced events and stop.
    /// </param>
    /// <returns>A result describing the final state after the watcher loop exits.</returns>
    Task<TrackingAgentOperationResult> StartAsync(CancellationToken cancellationToken);
}
