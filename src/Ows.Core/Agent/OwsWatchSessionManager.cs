using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Ows.Core.Events;
using Ows.Core.Hashing;
using Ows.Core.Init;
using Ows.Core.Notarization;

namespace Ows.Core.Agent;

/// <summary>
/// Implements the shared watcher and session lifecycle manager.
/// Coordinates project initialization, background watching, timeline events, session states, and verification services.
/// </summary>
public sealed partial class OwsWatchSessionManager : IOwsWatchSessionManager {
    /// <summary>
    /// The JSON serializer options configured for read and write operations of configuration files.
    /// </summary>
    private static readonly JsonSerializerOptions ConfigSerializerOptions = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// The active local tracking agent handling directory file changes and timeline synchronization.
    /// </summary>
    private LocalTrackingAgent? _activeAgent;

    /// <summary>
    /// The cancellation token source governing the lifecycle of the active file watching processes.
    /// </summary>
    private CancellationTokenSource? _activeCts;

    /// <summary>
    /// Checks whether the specified project directory has been initialized with the .ows structure and configuration file.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <returns><see langword="true"/> if the project is initialized; otherwise, <see langword="false"/>.</returns>
    public bool IsProjectInitialized(string projectRoot) {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot)) return false;
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        return Directory.Exists(localFolder) && File.Exists(Path.Combine(localFolder, "config.json"));
    }

    /// <summary>
    /// Initializes a new project at the specified root path by creating the .ows folder and generating default configuration assets.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    public void InitializeProject(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        var initializer = new OwsProjectInitializer();
        initializer.Initialize(projectRoot);
    }

    /// <summary>
    /// Starts the project file watcher asynchronously, initializing scanning, recovery analysis, and background event loops.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="usePolling">Determines whether to fall back to a polling-based file watcher instead of native OS filesystem events.</param>
    /// <param name="debounceMs">The interval in milliseconds to debounce rapid consecutive file change notifications.</param>
    /// <returns>A task representing the asynchronous start operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown if a watcher process is already running for the specified project root.</exception>
    public async Task StartWatcherAsync(string projectRoot, bool usePolling = false, int debounceMs = 500) {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        Directory.CreateDirectory(localFolder);

        var watcherJsonPath = Path.Combine(localFolder, "watcher.json");
        var stopFilePath = Path.Combine(localFolder, "watcher.stop");

        if (WatcherStateStore.IsWatcherRunning(projectRoot)) {
            throw new InvalidOperationException("Watcher is already running for this project.");
        }

        bool wasInterrupted = false;
        WatcherProcessState? interruptedState = null;
        if (File.Exists(watcherJsonPath)) {
            try {
                var content = await File.ReadAllTextAsync(watcherJsonPath);
                interruptedState = JsonSerializer.Deserialize<WatcherProcessState>(content);
                wasInterrupted = true;
            } catch {
                /*ignored*/
            }

            WatcherStateStore.TryDeleteFile(watcherJsonPath);
        }

        if (File.Exists(stopFilePath)) {
            WatcherStateStore.TryDeleteFile(stopFilePath);
        }

        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        var state = new WatcherProcessState {
            Pid = currentProcess.Id,
            StartedAt = DateTimeOffset.UtcNow
        };
        await WatcherStateStore.WriteStateAsync(watcherJsonPath, state);

        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        // Background poller for watcher.stop file
        WatcherSessionLifecycleCoordinator.StartStopPoller(stopFilePath, _activeCts, token);

        // Background poller for session heartbeats (every 30 seconds)
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        WatcherSessionLifecycleCoordinator.StartHeartbeatPoller(sessionPath, projectRoot,
            root => SendHeartbeatAsync(root), token);

        var projectConfig = GetProjectConfig(projectRoot);
        var excludeDirs = projectConfig?.WatcherSettings?.ExcludeDirectories;

        _activeAgent = new LocalTrackingAgent(new NullLogger<LocalTrackingAgent>());
        await _activeAgent.PrepareAsync(new TrackingAgentOptions {
            ProjectRootPath = projectRoot,
            DatabasePath = Path.Combine(localFolder, "ows.db"),
            WatcherOptions = new FileWatcherOptions {
                UsePollingFallback = usePolling,
                DebounceIntervalMs = debounceMs,
                ExcludeDirectories = excludeDirs
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

    /// <summary>
    /// Stops the active watcher process for the specified project, cleaning up token sources and active handles.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <returns>A task representing the asynchronous stop operation.</returns>
    public async Task StopWatcherAsync(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var watcherJsonPath = Path.Combine(localFolder, "watcher.json");
        var stopFilePath = Path.Combine(localFolder, "watcher.stop");

        if (!File.Exists(watcherJsonPath)) return;

        var wasRunning = WatcherStateStore.IsWatcherRunning(projectRoot);
        if (wasRunning) {
            var host = Environment.GetEnvironmentVariable("OWS_HOST") ?? "cli";
            try {
                await AppendEventToTimelineAsync(projectRoot, OwsEventType.WatcherStopped, null, host, null,
                    new Dictionary<string, string>
                    {
                        { "reason", "user_requested" }
                    });
            } catch {
                // Do not fail watcher stop if event logging fails
            }
        } else {
            // stale PID / crash recovery: emit WatcherInterrupted
            try {
                var content = await File.ReadAllTextAsync(watcherJsonPath);
                var prevState = JsonSerializer.Deserialize<WatcherProcessState>(content);
                var host = Environment.GetEnvironmentVariable("OWS_HOST") ?? "cli";
                var metadata = new Dictionary<string, string>
                {
                    { "reason", "stale_pid_cleanup" }
                };
                if (prevState != null) {
                    metadata["previousPid"] = prevState.Pid.ToString();
                    metadata["previousStartedAt"] = prevState.StartedAt.ToString("o");
                }

                await AppendEventToTimelineAsync(projectRoot, OwsEventType.WatcherInterrupted, null, host, null,
                    metadata);
            } catch {
                /*ignored*/
            }
        }

        try {
            await File.WriteAllTextAsync(stopFilePath, "stop");
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }

        // Wait up to 3 seconds for clean exit
        for (var i = 0; i < 30; i++) {
            if (!File.Exists(watcherJsonPath)) {
                break;
            }

            await Task.Delay(100);
        }

        if (File.Exists(watcherJsonPath)) {
            await WatcherSessionLifecycleCoordinator.ForceKillWatcherProcessAsync(watcherJsonPath);
            WatcherStateStore.TryDeleteFile(watcherJsonPath);
        }

        if (File.Exists(stopFilePath)) {
            WatcherStateStore.TryDeleteFile(stopFilePath);
        }
    }

    /// <summary>
    /// Checks whether the watcher process is currently running for the specified project.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <returns><see langword="true"/> if the watcher is running; otherwise, <see langword="false"/>.</returns>
    public bool IsWatcherRunning(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        return WatcherStateStore.IsWatcherRunning(projectRoot);
    }

    /// <summary>
    /// Determines whether the watcher process for the specified project has crashed or was abnormally terminated.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <returns><see langword="true"/> if the watcher crashed; otherwise, <see langword="false"/>.</returns>
    public bool DidWatcherCrash(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var watcherJsonPath = Path.Combine(localFolder, "watcher.json");
        if (!File.Exists(watcherJsonPath)) return false;
        return !IsWatcherRunning(projectRoot);
    }

    /// <summary>
    /// Validates that the target project root path exists on the local filesystem.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <exception cref="DirectoryNotFoundException">Thrown if the path is null, empty, or doesn't refer to an existing directory.</exception>
    private void EnsureProjectDirectoryExists(string projectRoot) {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot)) {
            throw new DirectoryNotFoundException($"Project directory not found at: {projectRoot}");
        }
    }

    /// <summary>
    /// Begins a new remote verification session for the specified project root path.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="verifierUrlOverride">An optional URL parameter to override the configured remote verifier target.</param>
    /// <returns>A task returning the unique remote session identifier.</returns>
    public Task<string> StartSessionAsync(string projectRoot, string? verifierUrlOverride = null) {
        EnsureProjectDirectoryExists(projectRoot);
        var config = GetProjectConfig(projectRoot) ?? new OwsProjectConfig();
        return RemoteSessionCoordinator.StartSessionAsync(projectRoot, config, verifierUrlOverride);
    }

    /// <summary>
    /// Dispatches a session activity heartbeat to the remote verifier.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="verifierUrlOverride">An optional URL parameter to override the configured remote verifier target.</param>
    /// <returns>A task representing the asynchronous heartbeat process.</returns>
    public Task SendHeartbeatAsync(string projectRoot, string? verifierUrlOverride = null) {
        EnsureProjectDirectoryExists(projectRoot);
        return RemoteSessionCoordinator.SendHeartbeatAsync(projectRoot, verifierUrlOverride);
    }

    /// <summary>
    /// Computes and posts a new project timeline state checkpoint to the remote verification server.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <returns>A task returning the checkpoint verification sequence or identification tag.</returns>
    public Task<string> AddCheckpointAsync(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        return RemoteSessionCoordinator.AddCheckpointAsync(projectRoot);
    }

    /// <summary>
    /// Retrieves the current active session identifier from the local session database or file cache.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <returns>The active session ID string, or <see langword="null"/> if none exists or is readable.</returns>
    public string? GetCurrentSessionId(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        if (!File.Exists(sessionPath)) return null;
        try {
            var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(sessionPath));
            return state?.SessionId;
        } catch (IOException) {
            return null;
        } catch (UnauthorizedAccessException) {
            return null;
        } catch (JsonException) {
            return null;
        }
    }

    /// <summary>
    /// Resolves the remote verifier URL for the specified project, checking the active session state before falling back to static config.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <returns>The verifier service URL string, or <see langword="null"/> if undefined.</returns>
    public string? GetVerifierUrl(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        if (File.Exists(sessionPath)) {
            try {
                var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(sessionPath));
                if (!string.IsNullOrWhiteSpace(state?.VerifierUrl)) return state.VerifierUrl;
            } catch (IOException) {
            } catch (UnauthorizedAccessException) {
            } catch (JsonException) {
            }
        }

        var config = GetProjectConfig(projectRoot);
        return config?.VerifierUrl;
    }

    /// <summary>
    /// Loads and deserializes the configuration settings for the specified project.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <returns>The configured project details, or <see langword="null"/> if the configuration is missing or corrupt.</returns>
    public OwsProjectConfig? GetProjectConfig(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var configPath = Path.Combine(localFolder, "config.json");
        if (!File.Exists(configPath)) return null;

        try {
            return JsonSerializer.Deserialize<OwsProjectConfig>(File.ReadAllText(configPath), ConfigSerializerOptions);
        } catch (IOException) {
            return null;
        } catch (UnauthorizedAccessException) {
            return null;
        } catch (JsonException) {
            return null;
        }
    }

    /// <summary>
    /// Saves the provided configuration details to the local project configuration file directory.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="config">The updated configuration instance to serialize and commit.</param>
    public void SaveProjectConfig(string projectRoot, OwsProjectConfig config) {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        Directory.CreateDirectory(localFolder);
        var configPath = Path.Combine(localFolder, "config.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, ConfigSerializerOptions));
    }

    /// <summary>
    /// Reads the last checkpoint timestamp recorded in the local session descriptor.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <returns>The timestamp of the last checkpoint, or <see langword="null"/> if unavailable.</returns>
    public DateTimeOffset? GetLastCheckpointAt(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        if (!File.Exists(sessionPath)) return null;
        try {
            var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(sessionPath));
            return state?.LastCheckpointAt;
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        } catch (JsonException) {
        }

        return null;
    }

    /// <summary>
    /// Reads the last heartbeat confirmation timestamp recorded in the local session descriptor.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <returns>The timestamp of the last heartbeat, or <see langword="null"/> if unavailable.</returns>
    public DateTimeOffset? GetLastHeartbeatAt(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        if (!File.Exists(sessionPath)) return null;
        try {
            var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(sessionPath));
            return state?.LastHeartbeatAt;
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        } catch (JsonException) {
        }

        return null;
    }

    /// <summary>
    /// Synthesizes, packages, and signs all current workspace tracked assets and history events into an OWS package.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <returns>A task returning the path to the newly created package file.</returns>
    public Task<string> PackageProjectAsync(string projectRoot) {
        EnsureProjectDirectoryExists(projectRoot);
        return PackageCreationCoordinator.PackageProjectAsync(projectRoot, AppendEventToTimelineAsync);
    }

    /// <summary>
    /// Uploads a built project package file to the remote verifier service.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="packagePath">The absolute path to the package archive file to upload.</param>
    /// <param name="verifierUrlOverride">An optional URL parameter to override the configured remote verifier target.</param>
    /// <returns>A task returning the transaction status payload returned by the verifier.</returns>
    public Task<string> UploadPackageAsync(string projectRoot, string packagePath,
        string? verifierUrlOverride = null) {
        EnsureProjectDirectoryExists(projectRoot);
        var config = GetProjectConfig(projectRoot);
        return RemoteSessionCoordinator.UploadPackageAsync(projectRoot, packagePath, config, verifierUrlOverride);
    }

    /// <summary>
    /// Queries the validation status and verification details of an uploaded package.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="packageId">The identifier of the remote package.</param>
    /// <param name="verifierUrlOverride">An optional URL parameter to override the configured remote verifier target.</param>
    /// <returns>A task returning the verification response summary.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no verifier URL can be determined for the status query.</exception>
    public Task<string> QueryPackageStatusAsync(string projectRoot, string packageId,
        string? verifierUrlOverride = null) {
        EnsureProjectDirectoryExists(projectRoot);
        var verifierUrl = verifierUrlOverride ?? GetVerifierUrl(projectRoot);
        if (string.IsNullOrWhiteSpace(verifierUrl)) {
            throw new InvalidOperationException("No remote verifier URL configured for status query.");
        }

        return RemoteSessionCoordinator.QueryPackageStatusAsync(verifierUrl, packageId);
    }

    /// <summary>
    /// Appends a ProjectOpened event to the local timeline history, recording host identity and safety identification hashes.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="host">The name of the host editor, IDE, or tool triggering the project open action.</param>
    /// <param name="reason">An optional metadata explanation of the opening action trigger.</param>
    /// <returns>A task representing the asynchronous emission.</returns>
    public async Task EmitProjectOpenedAsync(string projectRoot, string host, string? reason = null) {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        string? sessionId = null;
        if (File.Exists(sessionPath)) {
            try {
                var state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(sessionPath));
                sessionId = state?.SessionId;
            } catch {
                /*ignored*/
            }
        }

        var hashService = new Sha256HashService();
        var safeProjectIdentifier = hashService.ComputeHash(projectRoot);

        var metadata = new Dictionary<string, string>
        {
            { "host", host },
            { "projectIdentifier", safeProjectIdentifier }
        };
        if (reason is not null) {
            metadata["reason"] = reason;
        }

        if (sessionId is not null) {
            metadata["sessionId"] = sessionId;
        }

        await AppendEventToTimelineAsync(projectRoot, OwsEventType.ProjectOpened, null, host, null, metadata);
    }

    /// <summary>
    /// Appends a ProjectClosed event to the local timeline history.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="host">The name of the host editor, IDE, or tool triggering the project close action.</param>
    /// <param name="reason">An optional metadata explanation of the closing action trigger.</param>
    /// <returns>A task representing the asynchronous emission.</returns>
    public async Task EmitProjectClosedAsync(string projectRoot, string host, string? reason = null) {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        string? sessionId = null;
        if (File.Exists(sessionPath)) {
            try {
                var state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(sessionPath));
                sessionId = state?.SessionId;
            } catch {
                /*ignored*/
            }
        }

        var hashService = new Sha256HashService();
        var safeProjectIdentifier = hashService.ComputeHash(projectRoot);

        var metadata = new Dictionary<string, string>
        {
            { "host", host },
            { "projectIdentifier", safeProjectIdentifier }
        };
        if (reason is not null) {
            metadata["reason"] = reason;
        }

        if (sessionId is not null) {
            metadata["sessionId"] = sessionId;
        }

        await AppendEventToTimelineAsync(projectRoot, OwsEventType.ProjectClosed, null, host, null, metadata);
    }

    /// <summary>
    /// Appends a generic validation or process execution event (e.g. Build, Test run) to the timeline log.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="eventType">The type of event being registered.</param>
    /// <param name="host">The host metadata tag identifying execution environment.</param>
    /// <param name="label">An optional descriptive label of the operation, which will be scrubbed of confidential credentials.</param>
    /// <param name="exitCode">An optional process exit code.</param>
    /// <param name="durationMs">An optional execution duration measurement in milliseconds.</param>
    /// <returns>A task representing the asynchronous emission.</returns>
    /// <exception cref="ArgumentException">Thrown if the eventType is not validated for generic emissions in this standard version.</exception>
    public async Task EmitGenericEventAsync(
        string projectRoot,
        OwsEventType eventType,
        string host,
        string? label = null,
        int? exitCode = null,
        long? durationMs = null) {
        EnsureProjectDirectoryExists(projectRoot);

        var allowedEventTypes = new[]
        {
            OwsEventType.BuildStarted,
            OwsEventType.BuildSucceeded,
            OwsEventType.BuildFailed,
            OwsEventType.TestExecuted,
            OwsEventType.ProgramExecuted
        };

        if (Array.IndexOf(allowedEventTypes, eventType) < 0) {
            throw new ArgumentException($"Event type '{eventType}' is not allowed for generic emission in v0.1.");
        }

        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        string? sessionId = null;
        if (File.Exists(sessionPath)) {
            try {
                var state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(sessionPath));
                sessionId = state?.SessionId;
            } catch {
                /*ignored*/
            }
        }

        var metadata = new Dictionary<string, string>
        {
            { "host", host }
        };
        if (!string.IsNullOrWhiteSpace(label)) {
            metadata["label"] = ScrubSecrets(label);
        }

        if (exitCode.HasValue) {
            metadata["exitCode"] = exitCode.Value.ToString();
        }

        if (durationMs.HasValue) {
            metadata["durationMs"] = durationMs.Value.ToString();
        }

        if (sessionId is not null) {
            metadata["sessionId"] = sessionId;
        }

        await AppendEventToTimelineAsync(projectRoot, eventType, null, host, null, metadata);
    }

    /// <summary>
    /// Redacts sensitive credential information, security tokens, passwords, and authorization sequences from inputs to prevent leakage.
    /// </summary>
    /// <param name="input">The raw text block potentially containing keys or credential identifiers.</param>
    /// <returns>A sanitized string where matching private elements have been replaced with a redacted marker.</returns>
    public static string ScrubSecrets(string? input) {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // Redact values matching common key/token/password assignments or headers
        var result = MyRegex().Replace(input, "$1$2[REDACTED]");

        // Redact Bearer tokens
        result = MyRegex1().Replace(result, "$1[REDACTED]");

        return result;
    }

    /// <summary>
    /// Writes a structured Open Work Standard event to the timeline file.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="eventType">The standard type classification of the timeline event.</param>
    /// <param name="relativePath">The project-relative file location corresponding to the event, if any.</param>
    /// <param name="toolName">The agent or tool module reporting this change.</param>
    /// <param name="bytesChanged">The quantity of changed bytes associated with the event.</param>
    /// <param name="metadata">A key-value dictionary of contextual metadata payloads.</param>
    /// <returns>A task representing the timeline write operation.</returns>
    private async Task AppendEventToTimelineAsync(
        string projectRoot,
        OwsEventType eventType,
        string? relativePath,
        string? toolName,
        long? bytesChanged,
        IReadOnlyDictionary<string, string> metadata) {
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var timelinePath = Path.Combine(localFolder, OwsConstants.TimelineFileName);
        var previousEventHash = TimelineEventAppender.ReadLastEventHash(timelinePath);

        var owsEvent = new OwsEvent {
            EventType = eventType,
            ProjectId = Path.GetFileName(projectRoot),
            RelativePath = relativePath,
            ToolName = toolName,
            BytesChanged = bytesChanged,
            Metadata = metadata
        };

        await TimelineEventAppender.AppendEventAsync(timelinePath, owsEvent, previousEventHash, CancellationToken.None);
    }

    /// <summary>
    /// Matches common key, secret, password, and credential assignments in string values.
    /// </summary>
    /// <returns>A <see cref="Regex"/> instance compiled for identifying secret definitions.</returns>
    [GeneratedRegex(
        @"(?i)(password|token|key|secret|credential|pwd|bearer|auth|signature)(\s*[:=]\s*|\s+)([a-zA-Z0-9_\-\.\~]{6,})",
        RegexOptions.None, "en-US")]
    private static partial Regex MyRegex();

    /// <summary>
    /// Matches HTTP authorization scheme Bearer tokens.
    /// </summary>
    /// <returns>A <see cref="Regex"/> instance compiled for identifying bearer tokens.</returns>
    [GeneratedRegex(@"(?i)(bearer\s+)([a-zA-Z0-9_\-\.\~]{8,})", RegexOptions.None, "en-US")]
    private static partial Regex MyRegex1();
}

/// <summary>
/// State tracking helper for watcher processes. Contains process details used to detect interrupted sessions.
/// </summary>
public sealed class WatcherProcessState {
    /// <summary>
    /// Gets or sets the operating system Process Identifier (PID) of the active watcher host.
    /// </summary>
    public int Pid { get; init; }

    /// <summary>
    /// Gets or sets the timestamp when the watcher process started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }
}
