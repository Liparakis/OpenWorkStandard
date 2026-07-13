using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Ows.Core.Events;
using Ows.Core.Init;

namespace Ows.Core.Agent;

/// <summary>
/// Coordinates local project initialization, filesystem observation, and offline packaging.
/// </summary>
public sealed class OwsWatchSessionManager : IOwsWatchSessionManager {
    private static readonly JsonSerializerOptions ConfigSerializerOptions = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private LocalTrackingAgent? _activeAgent;
    private CancellationTokenSource? _activeCts;

    /// <inheritdoc />
    public bool IsProjectInitialized(string projectRoot) {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot)) return false;
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        return Directory.Exists(localFolder) && File.Exists(Path.Combine(localFolder, "config.json"));
    }

    /// <inheritdoc />
    public void InitializeProject(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        new OwsProjectInitializer().Initialize(projectRoot);
    }

    /// <inheritdoc />
    public async Task StartWatcherAsync(string projectRoot, bool usePolling = false, int debounceMs = 500) {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        Directory.CreateDirectory(localFolder);

        var watcherJsonPath = Path.Combine(localFolder, "watcher.json");
        var stopFilePath = Path.Combine(localFolder, "watcher.stop");
        if (WatcherStateStore.IsWatcherRunning(projectRoot)) {
            throw new InvalidOperationException("Watcher is already running for this project.");
        }

        var wasInterrupted = false;
        WatcherProcessState? interruptedState = null;
        if (File.Exists(watcherJsonPath)) {
            try {
                interruptedState = JsonSerializer.Deserialize<WatcherProcessState>(
                    await File.ReadAllTextAsync(watcherJsonPath));
                wasInterrupted = true;
            } catch {
                // A corrupt stale state is treated as an interrupted watcher.
            }

            WatcherStateStore.TryDeleteFile(watcherJsonPath);
        }

        WatcherStateStore.TryDeleteFile(stopFilePath);
        await WatcherStateStore.WriteStateAsync(watcherJsonPath, new WatcherProcessState {
            Pid = System.Diagnostics.Process.GetCurrentProcess().Id,
            StartedAt = DateTimeOffset.UtcNow
        });

        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;
        WatcherSessionLifecycleCoordinator.StartStopPoller(stopFilePath, _activeCts, token);

        var projectConfig = GetProjectConfig(projectRoot);
        _activeAgent = new LocalTrackingAgent(new NullLogger<LocalTrackingAgent>());
        await _activeAgent.PrepareAsync(new TrackingAgentOptions {
            ProjectRootPath = projectRoot,
            DatabasePath = Path.Combine(localFolder, "ows.db"),
            WatcherOptions = new FileWatcherOptions {
                UsePollingFallback = usePolling,
                DebounceIntervalMs = debounceMs,
                ExcludeDirectories = projectConfig?.WatcherSettings?.ExcludeDirectories
            },
            WasInterrupted = wasInterrupted,
            InterruptedState = interruptedState
        }, token);

        try {
            await _activeAgent.StartAsync(token);
        } finally {
            WatcherStateStore.TryDeleteFile(watcherJsonPath);
            WatcherStateStore.TryDeleteFile(stopFilePath);
            _activeCts = null;
            _activeAgent = null;
        }
    }

    /// <inheritdoc />
    public async Task StopWatcherAsync(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var watcherJsonPath = Path.Combine(localFolder, "watcher.json");
        var stopFilePath = Path.Combine(localFolder, "watcher.stop");
        if (!File.Exists(watcherJsonPath)) return;

        if (WatcherStateStore.IsWatcherRunning(projectRoot)) {
            try {
                await AppendEventToTimelineAsync(projectRoot, OwsEventType.WatcherStopped, null,
                    Environment.GetEnvironmentVariable("OWS_HOST") ?? "agent", null,
                    new Dictionary<string, string> { ["reason"] = "user_requested" });
            } catch {
                // Shutdown must not be blocked by a best-effort lifecycle event.
            }
        } else {
            try {
                var previous = JsonSerializer.Deserialize<WatcherProcessState>(
                    await File.ReadAllTextAsync(watcherJsonPath));
                var metadata = new Dictionary<string, string> { ["reason"] = "stale_pid_cleanup" };
                if (previous is not null) {
                    metadata["previousPid"] = previous.Pid.ToString();
                    metadata["previousStartedAt"] = previous.StartedAt.ToString("o");
                }

                await AppendEventToTimelineAsync(projectRoot, OwsEventType.WatcherInterrupted, null,
                    Environment.GetEnvironmentVariable("OWS_HOST") ?? "agent", null, metadata);
            } catch {
                // Best-effort crash recovery event.
            }
        }

        try {
            await File.WriteAllTextAsync(stopFilePath, "stop");
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }

        for (var i = 0; i < 30 && File.Exists(watcherJsonPath); i++) {
            await Task.Delay(100);
        }

        if (File.Exists(watcherJsonPath)) {
            await WatcherSessionLifecycleCoordinator.ForceKillWatcherProcessAsync(watcherJsonPath);
            WatcherStateStore.TryDeleteFile(watcherJsonPath);
        }

        WatcherStateStore.TryDeleteFile(stopFilePath);
    }

    /// <inheritdoc />
    public bool IsWatcherRunning(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        return WatcherStateStore.IsWatcherRunning(projectRoot);
    }

    /// <inheritdoc />
    public bool DidWatcherCrash(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        var watcherJsonPath = Path.Combine(projectRoot, OwsConstants.LocalFolderName, "watcher.json");
        return File.Exists(watcherJsonPath) && !IsWatcherRunning(projectRoot);
    }

    /// <inheritdoc />
    public OwsProjectConfig? GetProjectConfig(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        var configPath = Path.Combine(projectRoot, OwsConstants.LocalFolderName, "config.json");
        if (!File.Exists(configPath)) return null;

        try {
            return JsonSerializer.Deserialize<OwsProjectConfig>(
                File.ReadAllText(configPath), ConfigSerializerOptions);
        } catch (IOException) {
            return null;
        } catch (UnauthorizedAccessException) {
            return null;
        } catch (JsonException) {
            return null;
        }
    }

    /// <inheritdoc />
    public void SaveProjectConfig(string projectRoot, OwsProjectConfig config) {
        ArgumentNullException.ThrowIfNull(config);
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        Directory.CreateDirectory(localFolder);
        File.WriteAllText(Path.Combine(localFolder, "config.json"),
            JsonSerializer.Serialize(config, ConfigSerializerOptions));
    }

    /// <inheritdoc />
    public Task<string> PackageProjectAsync(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        return PackageCreationCoordinator.PackageProjectAsync(projectRoot, AppendEventToTimelineAsync);
    }

    private static void EnsureProjectDirectoryExists(string projectRoot) {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot)) {
            throw new DirectoryNotFoundException($"Project directory not found at: {projectRoot}");
        }
    }

    private static async Task AppendEventToTimelineAsync(
        string projectRoot,
        OwsEventType eventType,
        string? relativePath,
        string? toolName,
        long? bytesChanged,
        IReadOnlyDictionary<string, string> metadata) {
        var timelinePath = Path.Combine(projectRoot, OwsConstants.LocalFolderName, OwsConstants.TimelineFileName);
        var previousEventHash = TimelineEventAppender.ReadLastEventHash(timelinePath);
        await TimelineEventAppender.AppendEventAsync(timelinePath, new OwsEvent {
            EventType = eventType,
            ProjectId = Path.GetFileName(projectRoot),
            RelativePath = relativePath,
            ToolName = toolName,
            BytesChanged = bytesChanged,
            Metadata = metadata
        }, previousEventHash, CancellationToken.None);
    }
}

/// <summary>
/// State tracking helper for watcher processes.
/// </summary>
public sealed class WatcherProcessState {
    /// <summary>Gets the operating system process identifier.</summary>
    public int Pid { get; init; }

    /// <summary>Gets when the watcher process started.</summary>
    public DateTimeOffset StartedAt { get; init; }
}
