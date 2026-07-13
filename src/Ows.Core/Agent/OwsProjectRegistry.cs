using System.Text.Json;

namespace Ows.Core.Agent;

/// <summary>
///     Stores the explicitly initialized project roots that the local OWS Agent may watch.
/// </summary>
public sealed class OwsProjectRegistry {
    /// <summary>
    ///     Static lock object to synchronize in-process access to the registry.
    /// </summary>
    private static readonly object InProcessLock = new();

    /// <summary>
    ///     Serialization options configured for registry serialization/deserialization.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    ///     Initializes a registry backed by the supplied path or the platform default.
    /// </summary>
    /// <param name="registryPath">An optional custom path to the registry file.</param>
    public OwsProjectRegistry(string? registryPath = null) {
        RegistryPath = registryPath ?? GetDefaultRegistryPath();
    }

    /// <summary>
    ///     Gets the registry file path.
    /// </summary>
    public string RegistryPath { get; }

    /// <summary>
    ///     Registers an existing initialized project root. Registration is idempotent.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if the project was newly registered; otherwise, <see langword="false" /> if it was
    ///     already registered.
    /// </returns>
    /// <param name="projectRootPath">The project root directory path to register.</param>
    public bool Register(string projectRootPath) {
        var normalizedPath = NormalizeProjectRoot(projectRootPath);
        lock (InProcessLock) {
            using var registryLock = AcquireLock();
            var projects = ReadProjects();
            if (projects.Any(project => PathsEqual(project.ProjectRootPath, normalizedPath))) {
                return false;
            }

            projects.Add(
                new RegisteredOwsProject {
                    ProjectRootPath = normalizedPath,
                    RegisteredAtUtc = DateTimeOffset.UtcNow
                }
            );
            WriteProjects(projects);
            return true;
        }
    }

    /// <summary>
    ///     Removes a project root from the registry.
    /// </summary>
    /// <returns><see langword="true" /> if the project was removed; otherwise, <see langword="false" /> if it was not found.</returns>
    /// <param name="projectRootPath">The project root directory path to unregister.</param>
    public bool Unregister(string projectRootPath) {
        var normalizedPath = Path.GetFullPath(projectRootPath);
        lock (InProcessLock) {
            using var registryLock = AcquireLock();
            var projects = ReadProjects();
            var removed = projects.RemoveAll(project => PathsEqual(project.ProjectRootPath, normalizedPath)) > 0;
            if (removed) {
                WriteProjects(projects);
            }

            return removed;
        }
    }

    /// <summary>
    ///     Reads all registered projects, including roots that no longer exist.
    /// </summary>
    /// <returns>A read-only list of registered projects.</returns>
    public IReadOnlyList<RegisteredOwsProject> GetProjects() {
        lock (InProcessLock) {
            using var registryLock = AcquireLock();
            return ReadProjects();
        }
    }

    /// <summary>
    ///     Removes roots that no longer exist so moved or deleted projects are not watched.
    /// </summary>
    /// <returns>The number of missing projects removed from the registry.</returns>
    public int RemoveMissingProjects() {
        lock (InProcessLock) {
            using var registryLock = AcquireLock();
            var projects = ReadProjects();
            var removed = projects.RemoveAll(project => !Directory.Exists(project.ProjectRootPath));
            if (removed > 0) {
                WriteProjects(projects);
            }

            return removed;
        }
    }

    /// <summary>
    ///     Gets the default registry path for the current platform. Windows uses a
    ///     machine-scoped location so the LocalSystem Agent service and CLI share it.
    /// </summary>
    /// <returns>The default file path to the OWS agent project registry JSON file.</returns>
    public static string GetDefaultRegistryPath() {
        var configuredPath = Environment.GetEnvironmentVariable("OWS_AGENT_REGISTRY_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath)) {
            return Path.GetFullPath(configuredPath);
        }

        var folder = OperatingSystem.IsWindows()
            ? Environment.SpecialFolder.CommonApplicationData
            : Environment.SpecialFolder.LocalApplicationData;
        var localDataPath = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(localDataPath)) {
            localDataPath = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(localDataPath, "OpenWorkStandard", "projects.json");
    }

    /// <summary>
    ///     Acquires a file-based lock on the registry to prevent concurrent multi-process access.
    /// </summary>
    /// <returns>A <see cref="FileStream" /> representing the acquired lock file.</returns>
    private FileStream AcquireLock() {
        var directory = Path.GetDirectoryName(RegistryPath);
        if (string.IsNullOrWhiteSpace(directory)) {
            throw new InvalidOperationException("OWS Agent registry path must include a directory.");
        }

        Directory.CreateDirectory(directory);
        var lockPath = RegistryPath + ".lock";
        for (var attempt = 0; ; attempt++) {
            try {
                return File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            } catch (IOException) when (attempt < 19) {
                // ponytail: bounded cross-process retry; replace with an OS lock wait only if contention matters.
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>
    ///     Reads the list of registered projects from the registry file.
    /// </summary>
    /// <returns>A list of registered <see cref="RegisteredOwsProject" /> objects.</returns>
    private List<RegisteredOwsProject> ReadProjects() {
        if (!File.Exists(RegistryPath)) {
            return [];
        }

        var json = File.ReadAllText(RegistryPath);
        return JsonSerializer.Deserialize<List<RegisteredOwsProject>>(json, SerializerOptions) ?? [];
    }

    /// <summary>
    ///     Writes the collection of registered projects to the registry file.
    /// </summary>
    /// <param name="projects">The collection of projects to serialize and write.</param>
    private void WriteProjects(IReadOnlyCollection<RegisteredOwsProject> projects) {
        var directory = Path.GetDirectoryName(RegistryPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"{Path.GetFileName(RegistryPath)}.{Guid.NewGuid():N}.tmp");
        try {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(projects, SerializerOptions));
            File.Move(temporaryPath, RegistryPath, true);
        } finally {
            if (File.Exists(temporaryPath)) {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    ///     Normalizes the project root path and verifies that the directory exists.
    /// </summary>
    /// <returns>The normalized absolute path of the project root.</returns>
    /// <param name="projectRootPath">The project root path to normalize.</param>
    private static string NormalizeProjectRoot(string projectRootPath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);
        var normalizedPath = Path.GetFullPath(projectRootPath);
        if (!Directory.Exists(normalizedPath)) {
            throw new DirectoryNotFoundException($"OWS project root does not exist: {normalizedPath}");
        }

        return normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    ///     Compares two paths for equality using platform-specific case-sensitivity.
    /// </summary>
    /// <returns><see langword="true" /> if the paths are equal; otherwise, <see langword="false" />.</returns>
    /// <param name="left">The first path to compare.</param>
    /// <param name="right">The second path to compare.</param>
    private static bool PathsEqual(string left, string right) {
        return string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal
        );
    }
}

/// <summary>
///     Represents one explicitly registered OWS project root.
/// </summary>
public sealed record RegisteredOwsProject {
    /// <summary>Gets the absolute project root path.</summary>
    public string ProjectRootPath { get; init; } = string.Empty;

    /// <summary>Gets when the project was registered.</summary>
    public DateTimeOffset RegisteredAtUtc { get; init; }
}
