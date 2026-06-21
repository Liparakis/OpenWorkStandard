namespace Ows.Core.Agent;

/// <summary>
/// Configures the local tracking agent.
/// </summary>
public sealed record TrackingAgentOptions {
    /// <summary>
    /// Gets the root path of the tracked project.
    /// </summary>
    public string ProjectRootPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the path to the local SQLite database file used for agent state storage.
    /// </summary>
    public string DatabasePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the options that control the file-system watcher behaviour, including
    /// debounce timing and whether to use the polling fallback instead of native OS signals.
    /// </summary>
    public FileWatcherOptions WatcherOptions { get; init; } = new();

    /// <summary>
    /// Gets whether the watcher was interrupted (e.g. crashed, stale PID).
    /// </summary>
    public bool WasInterrupted { get; init; }

    /// <summary>
    /// Gets the process state of the interrupted watcher.
    /// </summary>
    public WatcherProcessState? InterruptedState { get; init; }
}
