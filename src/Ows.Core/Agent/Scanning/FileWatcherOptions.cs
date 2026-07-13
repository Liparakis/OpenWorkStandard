namespace Ows.Core.Agent.Scanning;

/// <summary>
/// Configures the runtime behaviour of <see cref="OwsFileWatcher"/>.
/// </summary>
public sealed record FileWatcherOptions {
    /// <summary>
    /// Gets the minimum quiet time in milliseconds that must elapse after the last
    /// file-system notification for the same path before the event is emitted.
    /// Defaults to 500 ms, which coalesces rapid burst saves (e.g. auto-save + formatter)
    /// into a single <see cref="FileWatchEvent"/>.
    /// </summary>
    public int DebounceIntervalMs { get; init; } = 500;

    /// <summary>
    /// Gets the interval in milliseconds between polling scans when the polling
    /// fallback is active. Defaults to 5 000 ms (5 seconds).
    /// </summary>
    public int PollingIntervalMs { get; init; } = 5_000;

    /// <summary>
    /// Gets a value indicating whether the watcher should use the polling fallback
    /// exclusively instead of relying on <see cref="System.IO.FileSystemWatcher"/>.
    /// Useful on file systems where native OS signals are unreliable.
    /// </summary>
    public bool UsePollingFallback { get; init; }

    /// <summary>
    /// Gets custom directories to exclude from watching and scanning.
    /// </summary>
    public string[]? ExcludeDirectories { get; init; }
}
