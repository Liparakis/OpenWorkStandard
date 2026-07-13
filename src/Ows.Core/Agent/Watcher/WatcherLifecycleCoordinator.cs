using System.Diagnostics;
using System.Text.Json;

namespace Ows.Core.Agent.Watcher;

/// <summary>
///     Coordinates background checking loops and manages cleaning up active watcher processes.
/// </summary>
internal static class WatcherLifecycleCoordinator {
    /// <summary>
    ///     Starts a background polling loop that checks for a watcher.stop file to request clean cancellation of the watcher.
    /// </summary>
    /// <param name="stopFilePath">The absolute path to the watcher.stop signal file.</param>
    /// <param name="activeCts">The active watcher cancellation source to trigger cancellation.</param>
    /// <param name="token">A token to stop the background polling task itself.</param>
    public static void StartStopPoller(
        string stopFilePath,
        CancellationTokenSource activeCts,
        CancellationToken token
    ) {
        _ = Task.Run(
            async () => {
                while (!token.IsCancellationRequested) {
                    if (File.Exists(stopFilePath)) {
                        await activeCts.CancelAsync();
                        break;
                    }

                    try {
                        await Task.Delay(500, token);
                    } catch (OperationCanceledException) {
                        break;
                    }
                }
            }, token
        );
    }

    /// <summary>
    ///     Safely terminates any active watcher process indicated by the PID in the watcher JSON file.
    /// </summary>
    /// <param name="watcherJsonPath">The absolute path to the watcher.json PID file.</param>
    /// <returns>A task representing the background cleanup process.</returns>
    public static async Task ForceKillWatcherProcessAsync(string watcherJsonPath) {
        if (!File.Exists(watcherJsonPath)) return;

        try {
            var content = await File.ReadAllTextAsync(watcherJsonPath);
            var state = JsonSerializer.Deserialize<WatcherProcessState>(content);
            if (state != null) {
                var proc = Process.GetProcessById(state.Pid);
                if (!proc.HasExited) {
                    proc.Kill(true);
                }
            }
        } catch (ArgumentException) {
        } catch (InvalidOperationException) {
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }
    }
}
