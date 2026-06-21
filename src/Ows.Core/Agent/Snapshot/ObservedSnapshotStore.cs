using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ows.Core.Agent;

/// <summary>
/// Represents the result of an observed snapshot loading operation, including readability and hashing flags.
/// </summary>
internal sealed class LoadSnapshotResult
{
    /// <summary>
    /// Gets the loaded <see cref="ObservedSnapshot"/> instance when successful; otherwise, <see langword="null"/>.
    /// </summary>
    public ObservedSnapshot? Snapshot { get; init; }

    /// <summary>
    /// Gets a value indicating whether the snapshot file exists on disk.
    /// </summary>
    public bool HadSnapshotFile { get; init; }

    /// <summary>
    /// Gets a value indicating whether the snapshot file existed but failed to load or parse (corrupt).
    /// </summary>
    public bool SnapshotUnreadable { get; init; }

    /// <summary>
    /// Gets the computed hash of the loaded snapshot, or <see langword="null"/> if not loaded.
    /// </summary>
    public string? ComputedSnapshotHash { get; init; }
}

/// <summary>
/// Provides atomic persistence and retrieval of project observed snapshot states on the local file system.
/// </summary>
internal static class ObservedSnapshotStore
{
    /// <summary>
    /// Serialization options to save the snapshot file formatted with indentations.
    /// </summary>
    private static readonly JsonSerializerOptions SnapshotSerializerOptions = new() { WriteIndented = true };

    /// <summary>
    /// Loads the observed snapshot from disk, handling missing or corrupt file scenarios.
    /// </summary>
    /// <param name="snapshotPath">The absolute path to the snapshot file.</param>
    /// <param name="logger">The logger instance to report warnings/errors.</param>
    /// <param name="cancellationToken">Token to cancel the load operation.</param>
    /// <returns>A <see cref="LoadSnapshotResult"/> summarizing the outcome of the loading process.</returns>
    public static async Task<LoadSnapshotResult> LoadSnapshotAsync(string snapshotPath, ILogger logger, CancellationToken cancellationToken)
    {
        var hadSnapshotFile = File.Exists(snapshotPath);
        var snapshotUnreadable = false;
        ObservedSnapshot? previousSnapshot = null;
        string? computedSnapshotHash = null;

        if (hadSnapshotFile)
        {
            try
            {
                var content = await File.ReadAllTextAsync(snapshotPath, cancellationToken);
                previousSnapshot = JsonSerializer.Deserialize<ObservedSnapshot>(content);
                if (previousSnapshot != null)
                {
                    computedSnapshotHash = SnapshotHashCalculator.ComputeHash(previousSnapshot);
                }
            }
            catch (Exception ex)
            {
                snapshotUnreadable = true;
                logger.LogWarning("Failed to parse observed snapshot: {Message}. Treating as corrupted and running clean scan.", ex.Message);
                try { File.Delete(snapshotPath); } catch { /*ignored*/ }
            }
        }

        return new LoadSnapshotResult
        {
            Snapshot = previousSnapshot,
            HadSnapshotFile = hadSnapshotFile,
            SnapshotUnreadable = snapshotUnreadable,
            ComputedSnapshotHash = computedSnapshotHash
        };
    }

    /// <summary>
    /// Persists the snapshot state to disk atomically using a temp file write and atomic replace.
    /// </summary>
    /// <param name="snapshotPath">The absolute path where the snapshot should be saved.</param>
    /// <param name="snapshot">The snapshot model state to persist.</param>
    /// <param name="cancellationToken">Token to cancel the save operation.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    public static async Task SaveSnapshotAtomicallyAsync(string snapshotPath, ObservedSnapshot snapshot, CancellationToken cancellationToken)
    {
        var tempPath = snapshotPath + ".tmp";
        var directory = Path.GetDirectoryName(snapshotPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(snapshot, SnapshotSerializerOptions);

        await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        await using (var writer = new StreamWriter(fs, System.Text.Encoding.UTF8))
        {
            await writer.WriteAsync(json);
            await writer.FlushAsync(cancellationToken);
            await fs.FlushAsync(cancellationToken);
        }

        // Atomic move
        File.Move(tempPath, snapshotPath, overwrite: true);
    }
}