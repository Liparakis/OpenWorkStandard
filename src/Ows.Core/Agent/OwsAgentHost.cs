namespace Ows.Core.Agent;

/// <summary>
/// Runs one existing OWS watcher manager for each registered initialized project.
/// </summary>
public sealed class OwsAgentHost {
    private readonly OwsProjectRegistry _registry;
    private readonly bool _usePolling;
    private readonly int _debounceMs;

    /// <summary>
    /// Initializes the local agent host.
    /// </summary>
    public OwsAgentHost(OwsProjectRegistry registry, bool usePolling = false, int debounceMs = 500) {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _usePolling = usePolling;
        _debounceMs = debounceMs;
    }

    /// <summary>
    /// Watches all currently registered projects until cancellation.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken) {
        var running = new Dictionary<string, RunningProject>(GetPathComparer());
        using var hostCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = hostCancellation.Token;
        var ipcTask = new OwsAgentIpcServer(_registry).RunAsync(token);
        try {
            while (!token.IsCancellationRequested) {
                _registry.RemoveMissingProjects();
                foreach (var project in _registry.GetProjects()) {
                    if (!Directory.Exists(project.ProjectRootPath) ||
                        !IsInitialized(project.ProjectRootPath) ||
                        running.ContainsKey(project.ProjectRootPath)) {
                        continue;
                    }

                    var manager = new OwsWatchSessionManager();
                    running[project.ProjectRootPath] = new RunningProject(project.ProjectRootPath, manager,
                        manager.StartWatcherAsync(project.ProjectRootPath, _usePolling, _debounceMs));
                }

                foreach (var completed in running.Where(entry => entry.Value.Task.IsCompleted).ToArray()) {
                    Observe(completed.Value.Task);
                    running.Remove(completed.Key);
                }

                // ponytail: polling the small local registry; replace with an OS-specific signal only when needed.
                await Task.Delay(TimeSpan.FromMilliseconds(250), token);
            }
        } catch (OperationCanceledException) when (token.IsCancellationRequested) {
        } finally {
            hostCancellation.Cancel();
            foreach (var entry in running.Values) {
                await StopAsync(entry);
            }

            Observe(ipcTask);
        }
    }

    private static bool IsInitialized(string projectRootPath) =>
        Directory.Exists(Path.Combine(projectRootPath, OwsConstants.LocalFolderName)) &&
        File.Exists(Path.Combine(projectRootPath, OwsConstants.LocalFolderName, "config.json"));

    private static async Task StopAsync(RunningProject project) {
        if (!project.Task.IsCompleted) {
            try {
                await project.Manager.StopWatcherAsync(project.ProjectRootPath);
            } catch {
                // The watcher task remains the source of truth for shutdown completion.
            }
        }

        Observe(project.Task);
    }

    private static void Observe(Task task) {
        try {
            task.GetAwaiter().GetResult();
        } catch {
            // A failed project watcher must not terminate the other registered projects.
        }
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed class RunningProject(string projectRootPath, OwsWatchSessionManager manager, Task task) {
        public string ProjectRootPath { get; } = projectRootPath;
        public OwsWatchSessionManager Manager { get; } = manager;
        public Task Task { get; } = task;
    }
}
