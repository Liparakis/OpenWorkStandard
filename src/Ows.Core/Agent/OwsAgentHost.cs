namespace Ows.Core.Agent;

/// <summary>
/// Runs one OWS project Agent for each registered initialized project.
/// </summary>
public sealed class OwsAgentHost {
    /// <summary>
    /// The project registry containing the registered OWS projects.
    /// </summary>
    private readonly OwsProjectRegistry _registry;
    /// <summary>
    /// A value indicating whether to use polling for file watchers.
    /// </summary>
    private readonly bool _usePolling;
    /// <summary>
    /// The debounce delay in milliseconds for file change events.
    /// </summary>
    private readonly int _debounceMs;

    /// <summary>
    /// Initializes the local agent host.
    /// </summary>
    /// <param name="registry">The project registry to check for registered projects.</param>
    /// <param name="usePolling">Whether to use polling file watchers.</param>
    /// <param name="debounceMs">The debounce interval in milliseconds for file changes.</param>
    public OwsAgentHost(OwsProjectRegistry registry, bool usePolling = false, int debounceMs = 500) {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _usePolling = usePolling;
        _debounceMs = debounceMs;
    }

    /// <summary>
    /// Watches all currently registered projects until cancellation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation of the runner loop.</returns>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
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

                    var manager = new OwsProjectAgent();
                    running[project.ProjectRootPath] = new RunningProject(
                        project.ProjectRootPath, manager,
                        manager.StartWatcherAsync(project.ProjectRootPath, _usePolling, _debounceMs)
                    );
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
            await hostCancellation.CancelAsync();
            foreach (var entry in running.Values) {
                await StopAsync(entry);
            }

            Observe(ipcTask);
        }
    }

    /// <summary>
    /// Determines whether the project at the specified root path has been initialized.
    /// </summary>
    /// <returns><see langword="true"/> if the project is initialized; otherwise, <see langword="false"/>.</returns>
    /// <param name="projectRootPath">The path to the project root directory.</param>
    private static bool IsInitialized(string projectRootPath) =>
        Directory.Exists(Path.Combine(projectRootPath, OwsConstants.LocalFolderName)) &&
        File.Exists(Path.Combine(projectRootPath, OwsConstants.LocalFolderName, "config.json"));

    /// <summary>
    /// Stops the watcher for a running project asynchronously.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the stop operation.</returns>
    /// <param name="project">The running project container.</param>
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

    /// <summary>
    /// Observes the completion of a task and catches any exception it throws to prevent runner crashes.
    /// </summary>
    /// <param name="task">The task to observe.</param>
    private static void Observe(Task task) {
        try {
            task.GetAwaiter().GetResult();
        } catch {
            // A failed project watcher must not terminate the other registered projects.
        }
    }

    /// <summary>
    /// Gets a path string comparer that matches OS case-sensitivity.
    /// </summary>
    /// <returns>An appropriate <see cref="StringComparer"/> for paths on the current platform.</returns>
    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Represents the <see cref="RunningProject"/> type.
    /// </summary>
    private sealed class RunningProject(string projectRootPath, OwsProjectAgent manager, Task task) {
        /// <summary>
        /// Gets the <see cref="ProjectRootPath"/> value.
        /// </summary>
        public string ProjectRootPath { get; } = projectRootPath;
        /// <summary>
        /// Gets the <see cref="Manager"/> value.
        /// </summary>
        public OwsProjectAgent Manager { get; } = manager;
        /// <summary>
        /// Gets the <see cref="Task"/> value.
        /// </summary>
        public Task Task { get; } = task;
    }
}
