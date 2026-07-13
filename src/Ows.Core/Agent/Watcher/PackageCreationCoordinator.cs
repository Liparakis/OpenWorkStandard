using System.IO.Compression;
using Ows.Core.Events;
using Ows.Core.Hashing;
using Ows.Core.Packaging;

namespace Ows.Core.Agent.Watcher;

/// <summary>
///     Coordinates the build package process and appends package creation events to the timeline.
/// </summary>
internal static class PackageCreationCoordinator {
    /// <summary>
    ///     Creates a zip archive package of the project and appends a PackageCreated event to the project timeline.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="appendEventFunc">Callback function to append events to the project timeline.</param>
    /// <returns>The path to the generated zip package file.</returns>
    /// <exception cref="InvalidOperationException">Thrown when package creation fails.</exception>
    public static async Task<string> PackageProjectAsync(
        string projectRoot,
        Func<string, OwsEventType, string?, string?, long?, IReadOnlyDictionary<string, string>, Task> appendEventFunc
    ) {
        var packagePath = Path.Combine(
            projectRoot,
            $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}"
        );
        var builder = new OwsPackageBuilder();
        var signingKeyPath = Environment.GetEnvironmentVariable("OWS_PACKAGE_SIGNING_KEY_PATH");
        var result = await OwsPackageBuilder.CreatePackageAsync(
            new PackageCreationRequest {
                ProjectRootPath = projectRoot,
                OutputPackagePath = packagePath,
                SignPackage = !string.IsNullOrWhiteSpace(signingKeyPath),
                SigningKeyPath = signingKeyPath
            }, CancellationToken.None
        );

        if (!result.Created) {
            throw new InvalidOperationException("Packaging failed.");
        }

        // Emit PackageCreated event locally
        try {
            var packageHash = string.Empty;
            long packageSize = 0;
            var artifactCount = 0;

            if (File.Exists(packagePath)) {
                packageSize = new FileInfo(packagePath).Length;
                packageHash = Sha256HashService.ComputeHash(await File.ReadAllBytesAsync(packagePath));
                using var archive = ZipFile.OpenRead(packagePath);
                artifactCount = archive.Entries.Count(entry =>
                    entry.FullName.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase)
                );
            }

            var metadata = new Dictionary<string, string> {
                { "packagePath", Path.GetFileName(packagePath) },
                { "packageHash", packageHash },
                { "packageSize", packageSize.ToString() },
                { "artifactCount", artifactCount.ToString() },
                { "createdAt", DateTimeOffset.UtcNow.ToString("o") }
            };
            var host = Environment.GetEnvironmentVariable("OWS_HOST") ?? "cli";
            await appendEventFunc(
                projectRoot, OwsEventType.PackageCreated, Path.GetFileName(packagePath),
                host, 0, metadata
            );
        } catch {
            // Failures in writing the event should not fail the package command itself
        }

        return packagePath;
    }
}
