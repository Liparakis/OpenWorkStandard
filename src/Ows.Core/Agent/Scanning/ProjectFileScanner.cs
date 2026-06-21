using Ows.Core.Hashing;

namespace Ows.Core.Agent;

/// <summary>
/// Provides helper functions to scan project files, apply folder exclusions, and calculate file hashes and line estimates.
/// </summary>
internal static class ProjectFileScanner {
    /// <summary>
    /// Default list of relative directory paths to exclude from scanning.
    /// </summary>
    private static readonly string[] DefaultExclusions =
    [
        ".ows",
        ".git",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "build",
        ".vs",
        "target",
        "coverage"
    ];

    /// <summary>
    /// Determines whether a given absolute file path should be excluded based on default exclusions and configured exclusions.
    /// </summary>
    /// <param name="absolutePath">The absolute path of the file to inspect.</param>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="excludeDirectories">An optional list of additional directory names to exclude.</param>
    /// <returns><see langword="true"/> if the file path should be excluded; otherwise, <see langword="false"/>.</returns>
    public static bool ShouldExclude(string absolutePath, string projectRoot, IEnumerable<string>? excludeDirectories) {
        // Check local .ows folder
        if (absolutePath.Contains(
                $"{Path.DirectorySeparatorChar}{OwsConstants.LocalFolderName}{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            absolutePath.Contains($"/{OwsConstants.LocalFolderName}/", StringComparison.Ordinal)) {
            return true;
        }

        // Parse path segments to check exclusions
        var relative = Path.GetRelativePath(projectRoot, absolutePath);
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        // Get exclusion list
        var exclusions = new List<string>(DefaultExclusions);
        if (excludeDirectories != null) {
            exclusions.AddRange(excludeDirectories);
        }

        foreach (var segment in segments) {
            foreach (var exclusion in exclusions) {
                if (string.Equals(segment, exclusion, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Scans the current project files and generates observed states containing size, hashes, and line estimates.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="excludeDirectories">An optional list of directory names to exclude.</param>
    /// <param name="hashService">The hash service to compute file SHA-256 signatures.</param>
    /// <returns>A dictionary mapping relative file paths to their <see cref="ObservedFileState"/> details.</returns>
    public static Dictionary<string, ObservedFileState> ScanCurrentFiles(string projectRoot,
        IEnumerable<string>? excludeDirectories, Sha256HashService hashService) {
        var files = new Dictionary<string, ObservedFileState>(StringComparer.OrdinalIgnoreCase);

        var trackedFiles = Directory
            .EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
            .Where(path => !ShouldExclude(path, projectRoot, excludeDirectories))
            .ToList();

        var now = DateTimeOffset.UtcNow;
        foreach (var path in trackedFiles) {
            var relative = Path.GetRelativePath(projectRoot, path);
            try {
                var info = new FileInfo(path);
                var size = info.Length;
                var lastWrite = info.LastWriteTimeUtc;
                var lineCount = GetLineCountEstimate(path);
                var fileHash = GetFileHash(path, hashService);

                files[relative] = new ObservedFileState {
                    RelativePath = relative,
                    FileHash = fileHash,
                    Size = size,
                    LineCount = lineCount,
                    LastWriteTime = lastWrite,
                    ObservedAt = now
                };
            } catch (IOException) {
                // File locked or missing, skip
            }
        }

        return files;
    }

    /// <summary>
    /// Computes a line count estimate by reading the file bytes and counting newline characters.
    /// </summary>
    /// <param name="path">The absolute path to the target file.</param>
    /// <returns>The number of lines in the file, or 0 if empty or unreadable.</returns>
    public static int GetLineCountEstimate(string path) {
        try {
            if (!File.Exists(path)) return 0;
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return 0;
            var count = 1;
            foreach (var t in bytes) {
                if (t == '\n') count++;
            }

            return count;
        } catch {
            return 0;
        }
    }

    /// <summary>
    /// Computes the SHA-256 hash of the specified file.
    /// </summary>
    /// <param name="path">The absolute path to the file.</param>
    /// <param name="hashService">The hash service used to generate the checksum.</param>
    /// <returns>A hex-encoded string of the file's hash, or an empty string if unreadable.</returns>
    public static string GetFileHash(string path, Sha256HashService hashService) {
        try {
            return !File.Exists(path) ? string.Empty : hashService.ComputeHash(File.ReadAllBytes(path));
        } catch {
            return string.Empty;
        }
    }
}
