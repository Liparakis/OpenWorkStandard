using Ows.Core.Init;

namespace Ows.Core.Agent;

/// <summary>
/// Coordinates local project initialization, Agent observation, and packaging.
/// </summary>
public interface IOwsWatchSessionManager {
    /// <summary>Gets whether the project contains initialized OWS metadata.</summary>
    bool IsProjectInitialized(string projectRoot);

    /// <summary>Initializes OWS metadata in the project root.</summary>
    void InitializeProject(string projectRoot);

    /// <summary>Starts local filesystem observation.</summary>
    Task StartWatcherAsync(string projectRoot, bool usePolling = false, int debounceMs = 500);

    /// <summary>Stops local filesystem observation.</summary>
    Task StopWatcherAsync(string projectRoot);

    /// <summary>Gets whether the local watcher is running.</summary>
    bool IsWatcherRunning(string projectRoot);

    /// <summary>Gets whether a stale watcher state indicates an interrupted watcher.</summary>
    bool DidWatcherCrash(string projectRoot);

    /// <summary>Loads local project configuration.</summary>
    OwsProjectConfig? GetProjectConfig(string projectRoot);

    /// <summary>Saves local project configuration.</summary>
    void SaveProjectConfig(string projectRoot, OwsProjectConfig config);

    /// <summary>Creates an offline package from the current project evidence.</summary>
    Task<string> PackageProjectAsync(string projectRoot);
}
