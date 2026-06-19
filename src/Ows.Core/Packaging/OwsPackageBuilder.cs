using System.IO.Compression;
using System.Text.Json;

using Ows.Core.Hashing;

namespace Ows.Core.Packaging;

/// <summary>
/// Provides the initial OWS package builder skeleton.
/// </summary>
public sealed class OwsPackageBuilder : IPackageBuilder
{
    /// <inheritdoc />
    public Task<PackageCreationResult> CreatePackageAsync(PackageCreationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var localFolder = Path.Combine(request.ProjectRootPath, OwsConstants.LocalFolderName);
        var timelinePath = Path.Combine(localFolder, OwsConstants.TimelineFileName);
        var receiptsPath = Path.Combine(localFolder, OwsConstants.ReceiptsFileName);
        var outputDirectory = Path.GetDirectoryName(request.OutputPackagePath);
        var hashService = new Sha256HashService();

        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var timelineText = File.ReadAllText(timelinePath);
        const string versionGraphText = "{\"nodes\":[],\"edges\":[]}";
        var artifactHashes = Directory
            .EnumerateFiles(request.ProjectRootPath, "*", SearchOption.AllDirectories)
            .Select(filePath => new
            {
                FilePath = filePath,
                RelativePath = Path.GetRelativePath(request.ProjectRootPath, filePath)
            })
            .Where(file => !string.Equals(file.FilePath, request.OutputPackagePath, StringComparison.OrdinalIgnoreCase) &&
                !file.RelativePath.StartsWith($"{OwsConstants.LocalFolderName}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                file => $"artifacts/{file.RelativePath.Replace('\\', '/')}",
                file => hashService.ComputeHash(File.ReadAllBytes(file.FilePath)),
                StringComparer.Ordinal);

        var manifest = new OwsManifest
        {
            ProjectName = Path.GetFileName(request.ProjectRootPath),
            Platform = Environment.OSVersion.Platform.ToString(),
            TrackedPath = request.ProjectRootPath,
            TimelineHash = hashService.ComputeHash(timelineText),
            VersionGraphHash = hashService.ComputeHash(versionGraphText),
            ArtifactHashes = artifactHashes
        };

        if (File.Exists(request.OutputPackagePath))
        {
            File.Delete(request.OutputPackagePath);
        }

        using (var archive = ZipFile.Open(request.OutputPackagePath, ZipArchiveMode.Create))
        {
            var manifestEntry = archive.CreateEntry(OwsConstants.ManifestFileName);
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }

            var timelineEntry = archive.CreateEntry(OwsConstants.TimelineFileName);
            using (var timelineWriter = new StreamWriter(timelineEntry.Open()))
            {
                timelineWriter.Write(timelineText);
            }

            var graphEntry = archive.CreateEntry(OwsConstants.VersionGraphFileName);
            using (var graphWriter = new StreamWriter(graphEntry.Open()))
            {
                graphWriter.Write(versionGraphText);
            }

            if (File.Exists(receiptsPath))
            {
                archive.CreateEntryFromFile(receiptsPath, OwsConstants.ReceiptsFileName);
            }

            foreach (var artifactPath in artifactHashes.Keys)
            {
                var relativePath = artifactPath["artifacts/".Length..].Replace('/', Path.DirectorySeparatorChar);
                archive.CreateEntryFromFile(Path.Combine(request.ProjectRootPath, relativePath), artifactPath);
            }
        }

        return Task.FromResult(new PackageCreationResult
        {
            Created = true,
            OutputPackagePath = request.OutputPackagePath,
            Message = $"OWS package created at {request.OutputPackagePath}"
        });
    }
}
