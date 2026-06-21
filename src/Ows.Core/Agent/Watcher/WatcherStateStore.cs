using System.Text.Json;

namespace Ows.Core.Agent;

/// <summary>
/// Provides utility functions to check if the watcher is running on the OS and manage the watcher state file.
/// </summary>
internal static class WatcherStateStore {
    /// <summary>
    /// Serialization settings to format json files.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>
    /// Known process names of the OWS watcher CLI and test runners.
    /// </summary>
    private static readonly string[] KnownWatcherProcessNames = ["ows", "dotnet", "testhost"];

    /// <summary>
    /// Writes the watcher process state metadata atomically to watcher.json.
    /// </summary>
    /// <param name="watcherJsonPath">The absolute path to the watcher.json PID file.</param>
    /// <param name="state">The process state metadata to serialize.</param>
    /// <returns>A task representing the write operation.</returns>
    public static async Task WriteStateAsync(string watcherJsonPath, WatcherProcessState state) {
        await File.WriteAllTextAsync(watcherJsonPath, JsonSerializer.Serialize(state, SerializerOptions));
    }

    /// <summary>
    /// Inspects whether the watcher process referenced in the local project's watcher.json is still running.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <returns><see langword="true"/> if a watcher process exists and matches active watcher names; otherwise, <see langword="false"/>.</returns>
    public static bool IsWatcherRunning(string projectRoot) {
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var watcherJsonPath = Path.Combine(localFolder, "watcher.json");
        if (!File.Exists(watcherJsonPath)) return false;

        try {
            var content = File.ReadAllText(watcherJsonPath);
            var state = JsonSerializer.Deserialize<WatcherProcessState>(content);
            if (state == null) return false;

            var proc = System.Diagnostics.Process.GetProcessById(state.Pid);
            if (proc.HasExited) return false;

            var procName = proc.ProcessName.ToLowerInvariant();
            return KnownWatcherProcessNames.Any(procName.Contains);
        } catch (ArgumentException) {
            return false;
        } catch (InvalidOperationException) {
            return false;
        } catch (IOException) {
            return false;
        } catch (UnauthorizedAccessException) {
            return false;
        } catch (JsonException) {
            return false;
        }
    }

    /// <summary>
    /// Attempts to safely delete a file, swallowing common IO access exceptions.
    /// </summary>
    /// <param name="path">The absolute path to the file to delete.</param>
    public static void TryDeleteFile(string path) {
        try {
            File.Delete(path);
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        }
    }
}
