using Ows.Core.Hashing;

namespace Ows.Core.Packaging;

/// <summary>
/// Provides the OWS package builder that delegates to focused helper services.
/// </summary>
public sealed class OwsPackageBuilder : IPackageBuilder
{
    /// <inheritdoc />
    public Task<PackageCreationResult> CreatePackageAsync(PackageCreationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var localFolder = Path.Combine(request.ProjectRootPath, OwsConstants.LocalFolderName);
        var timelinePath = Path.Combine(localFolder, OwsConstants.TimelineFileName);
        var receiptsPath = Path.Combine(localFolder, OwsConstants.ReceiptsFileName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        var outputDirectory = Path.GetDirectoryName(request.OutputPackagePath);
        var hashService = new Sha256HashService();

        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var timelineText = File.ReadAllText(timelinePath);
        const string versionGraphText = "{\"nodes\":[],\"edges\":[]}";
        var sessionText = File.Exists(sessionPath) ? File.ReadAllText(sessionPath) : null;

        var artifactHashes = PackageArtifactCollector.CollectArtifacts(
            request.ProjectRootPath, request.OutputPackagePath, hashService);

        var manifest = PackageManifestBuilder.BuildManifest(
            request.ProjectRootPath, timelineText, versionGraphText, sessionText, artifactHashes, hashService);

        PackageArchiveWriter.WriteArchive(
            request.OutputPackagePath,
            request.ProjectRootPath,
            manifest,
            timelineText,
            versionGraphText,
            receiptsPath,
            sessionPath,
            sessionText is not null,
            artifactHashes);

        return Task.FromResult(new PackageCreationResult
        {
            Created = true
        });
    }
}