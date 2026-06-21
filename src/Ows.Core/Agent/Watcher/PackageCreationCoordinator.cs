using System.IO.Compression;
using System.Text.Json;
using Ows.Core.Events;
using Ows.Core.Hashing;
using Ows.Core.Notarization;
using Ows.Core.Packaging;

namespace Ows.Core.Agent;

/// <summary>
/// Coordinates the build package process and appends package creation events to the timeline.
/// </summary>
internal static class PackageCreationCoordinator {
    /// <summary>
    /// Creates a zip archive package of the project and appends a PackageCreated event to the project timeline.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="appendEventFunc">Callback function to append events to the project timeline.</param>
    /// <returns>The path to the generated zip package file.</returns>
    /// <exception cref="InvalidOperationException">Thrown when package creation fails.</exception>
    public static async Task<string> PackageProjectAsync(
        string projectRoot,
        Func<string, OwsEventType, string?, string?, long?, IReadOnlyDictionary<string, string>, Task> appendEventFunc) {
        var packagePath = Path.Combine(projectRoot,
            $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}");
        var builder = new OwsPackageBuilder();
        var result = await builder.CreatePackageAsync(new PackageCreationRequest {
            ProjectRootPath = projectRoot,
            OutputPackagePath = packagePath
        }, CancellationToken.None);

        if (!result.Created) {
            throw new InvalidOperationException("Packaging failed.");
        }

        // Emit PackageCreated event locally
        try {
            var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
            string? sessionId = null;
            if (File.Exists(sessionPath)) {
                var state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(sessionPath));
                sessionId = state?.SessionId;
            }

            var packageHash = string.Empty;
            long packageSize = 0;
            var artifactCount = 0;

            if (File.Exists(packagePath)) {
                packageSize = new FileInfo(packagePath).Length;
                packageHash = new Sha256HashService().ComputeHash(await File.ReadAllBytesAsync(packagePath));
                using var archive = ZipFile.OpenRead(packagePath);
                artifactCount = archive.Entries.Count(entry =>
                    entry.FullName.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase));
            }

            var metadata = new Dictionary<string, string>
            {
                { "packagePath", Path.GetFileName(packagePath) },
                { "packageHash", packageHash },
                { "packageSize", packageSize.ToString() },
                { "artifactCount", artifactCount.ToString() },
                { "createdAt", DateTimeOffset.UtcNow.ToString("o") }
            };
            if (sessionId is not null) {
                metadata["sessionId"] = sessionId;
            }

            var host = Environment.GetEnvironmentVariable("OWS_HOST") ?? "cli";
            await appendEventFunc(projectRoot, OwsEventType.PackageCreated, Path.GetFileName(packagePath),
                host, 0, metadata);
        } catch {
            // Failures in writing the event should not fail the package command itself
        }

        return packagePath;
    }
}
