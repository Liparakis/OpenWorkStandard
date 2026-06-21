namespace Ows.Core.Agent;

/// <summary>
/// Represents the result of comparing a previous snapshot to the current files on disk.
/// </summary>
internal sealed class RecoveryScanResult {
    /// <summary>
    /// Gets the list of files created since the previous snapshot.
    /// </summary>
    public required List<ObservedFileState> CreatedFiles { get; init; }

    /// <summary>
    /// Gets the list of modified files, pairing their previous and current states.
    /// </summary>
    public required List<(ObservedFileState Prev, ObservedFileState Curr)> ModifiedFiles { get; init; }

    /// <summary>
    /// Gets the list of files deleted since the previous snapshot.
    /// </summary>
    public required List<ObservedFileState> DeletedFiles { get; init; }

    /// <summary>
    /// Gets a value indicating whether there are any created, modified, or deleted files.
    /// </summary>
    public bool HasDifferences => CreatedFiles.Count > 0 || ModifiedFiles.Count > 0 || DeletedFiles.Count > 0;
}

/// <summary>
/// Provides comparison services between file snapshots and checks delta thresholds for unobserved changes.
/// </summary>
internal static class RecoveryScanService {
    /// <summary>
    /// Compares a previous snapshot with the current scan of files to identify changes.
    /// </summary>
    /// <param name="previousSnapshot">The baseline observed snapshot.</param>
    /// <param name="currentFiles">The current state of tracked files.</param>
    /// <returns>A <see cref="RecoveryScanResult"/> listing created, modified, and deleted files.</returns>
    public static RecoveryScanResult CompareSnapshots(
        ObservedSnapshot previousSnapshot,
        Dictionary<string, ObservedFileState> currentFiles) {
        var createdFiles = new List<ObservedFileState>();
        var modifiedFiles = new List<(ObservedFileState Prev, ObservedFileState Curr)>();
        var deletedFiles = new List<ObservedFileState>();

        foreach (var file in currentFiles) {
            if (!previousSnapshot.Files.TryGetValue(file.Key, out var prev)) {
                createdFiles.Add(file.Value);
            } else if (!string.Equals(prev.FileHash, file.Value.FileHash, StringComparison.Ordinal) || prev.Size != file.Value.Size) {
                modifiedFiles.Add((prev, file.Value));
            }
        }

        foreach (var file in previousSnapshot.Files) {
            if (!currentFiles.ContainsKey(file.Key)) {
                deletedFiles.Add(file.Value);
            }
        }

        return new RecoveryScanResult {
            CreatedFiles = createdFiles,
            ModifiedFiles = modifiedFiles,
            DeletedFiles = deletedFiles
        };
    }

    /// <summary>
    /// Checks if a file delta exceeds size or line thresholds to be classified as a large change.
    /// </summary>
    /// <param name="bytesDelta">The change in bytes (current size minus previous size).</param>
    /// <param name="linesDelta">The change in estimated line count.</param>
    /// <returns><see langword="true"/> if the change is considered large; otherwise, <see langword="false"/>.</returns>
    public static bool IsLargeChange(long bytesDelta, int linesDelta) {
        if (!IsLargeUnobservedChangeEnabled()) return false;
        var byteThreshold = GetLargeUnobservedChangeByteThreshold();
        var lineThreshold = GetLargeUnobservedChangeLineThreshold();
        return Math.Abs(bytesDelta) >= byteThreshold || Math.Abs(linesDelta) >= lineThreshold;
    }

    /// <summary>
    /// Reads from environment variables whether large unobserved change checks are enabled.
    /// </summary>
    /// <returns><see langword="true"/> if enabled (default); otherwise, <see langword="false"/>.</returns>
    private static bool IsLargeUnobservedChangeEnabled() {
        var envVal = Environment.GetEnvironmentVariable("OwsCapture:LargeUnobservedChange:Enabled")
                     ?? Environment.GetEnvironmentVariable("OwsCapture__LargeUnobservedChange__Enabled")
                     ?? Environment.GetEnvironmentVariable("OWS_CAPTURE_LARGE_UNOBSERVED_CHANGE_ENABLED");
        if (bool.TryParse(envVal, out var enabled)) return enabled;
        return true;
    }

    /// <summary>
    /// Reads the byte threshold above which changes are classified as large unobserved changes.
    /// </summary>
    /// <returns>The byte threshold value (default is 50000).</returns>
    private static long GetLargeUnobservedChangeByteThreshold() {
        var envVal = Environment.GetEnvironmentVariable("OwsCapture:LargeUnobservedChange:ByteThreshold")
                     ?? Environment.GetEnvironmentVariable("OwsCapture__LargeUnobservedChange__ByteThreshold")
                     ?? Environment.GetEnvironmentVariable("OWS_CAPTURE_LARGE_UNOBSERVED_CHANGE_BYTETHRESHOLD");
        if (long.TryParse(envVal, out var threshold)) return threshold;
        return 50000;
    }

    /// <summary>
    /// Reads the line count threshold above which changes are classified as large unobserved changes.
    /// </summary>
    /// <returns>The line threshold value (default is 300).</returns>
    private static int GetLargeUnobservedChangeLineThreshold() {
        var envVal = Environment.GetEnvironmentVariable("OwsCapture:LargeUnobservedChange:LineThreshold")
                     ?? Environment.GetEnvironmentVariable("OwsCapture__LargeUnobservedChange__LineThreshold")
                     ?? Environment.GetEnvironmentVariable("OWS_CAPTURE_LARGE_UNOBSERVED_CHANGE_LINETHRESHOLD");
        if (int.TryParse(envVal, out var threshold)) return threshold;
        return 300;
    }
}
