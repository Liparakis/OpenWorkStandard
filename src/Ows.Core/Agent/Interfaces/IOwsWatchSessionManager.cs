using Ows.Core.Init;

namespace Ows.Core.Agent;

/// <summary>
/// Provides a unified manager for OWS watcher, local config, session, and packaging/upload operations.
/// </summary>
public interface IOwsWatchSessionManager
{
    /// <summary>
    /// Gets whether the OWS project is initialized (has .ows folder).
    /// </summary>
    bool IsProjectInitialized(string projectRoot);

    /// <summary>
    /// Initializes the OWS project in the specified root path.
    /// </summary>
    void InitializeProject(string projectRoot);

    /// <summary>
    /// Starts the file system watcher for the project.
    /// </summary>
    Task StartWatcherAsync(string projectRoot, bool usePolling = false, int debounceMs = 500);

    /// <summary>
    /// Stops the file system watcher for the project.
    /// </summary>
    Task StopWatcherAsync(string projectRoot);

    /// <summary>
    /// Gets whether the watcher is currently running.
    /// </summary>
    bool IsWatcherRunning(string projectRoot);

    /// <summary>
    /// Gets whether the watcher has crashed (watcher.json exists but process is dead).
    /// </summary>
    bool DidWatcherCrash(string projectRoot);

    /// <summary>
    /// Starts a session (local or remote based on config).
    /// </summary>
    Task<string> StartSessionAsync(string projectRoot, string? verifierUrlOverride = null);

    /// <summary>
    /// Sends a heartbeat to the remote verifier.
    /// </summary>
    Task SendHeartbeatAsync(string projectRoot, string? verifierUrlOverride = null);

    /// <summary>
    /// Takes a local checkpoint.
    /// </summary>
    Task<string> AddCheckpointAsync(string projectRoot);

    /// <summary>
    /// Gets the current session ID, if any.
    /// </summary>
    string? GetCurrentSessionId(string projectRoot);

    /// <summary>
    /// Gets the configured or session verifier URL.
    /// </summary>
    string? GetVerifierUrl(string projectRoot);

    /// <summary>
    /// Gets the assessment context.
    /// </summary>
    OwsProjectConfig? GetProjectConfig(string projectRoot);

    /// <summary>
    /// Saves the assessment context config.
    /// </summary>
    void SaveProjectConfig(string projectRoot, OwsProjectConfig config);

    /// <summary>
    /// Gets the timestamp of the last checkpoint.
    /// </summary>
    DateTimeOffset? GetLastCheckpointAt(string projectRoot);

    /// <summary>
    /// Gets the timestamp of the last heartbeat.
    /// </summary>
    DateTimeOffset? GetLastHeartbeatAt(string projectRoot);

    /// <summary>
    /// Packages the project.
    /// </summary>
    Task<string> PackageProjectAsync(string projectRoot);

    /// <summary>
    /// Uploads the package to the verifier.
    /// </summary>
    Task<string> UploadPackageAsync(string projectRoot, string packagePath, string? verifierUrlOverride = null);

    /// <summary>
    /// Queries the package verification status from the verifier.
    /// </summary>
    Task<string> QueryPackageStatusAsync(string projectRoot, string packageId, string? verifierUrlOverride = null);

    /// <summary>
    /// Emits a ProjectOpened event to the local timeline.
    /// </summary>
    Task EmitProjectOpenedAsync(string projectRoot, string host, string? reason = null);

    /// <summary>
    /// Emits a ProjectClosed event to the local timeline.
    /// </summary>
    Task EmitProjectClosedAsync(string projectRoot, string host, string? reason = null);

    /// <summary>
    /// Emits a generic command/action event to the local timeline.
    /// </summary>
    Task EmitGenericEventAsync(
        string projectRoot,
        Events.OwsEventType eventType,
        string host,
        string? label = null,
        int? exitCode = null,
        long? durationMs = null);
}
