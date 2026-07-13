using Ows.Core.Hashing;

namespace Ows.Core.Packaging.Helpers;

/// <summary>
/// Represents the <see cref="PackageManifestBuilder"/> type.
/// </summary>
internal static class PackageManifestBuilder {
    /// <summary>
    /// Builds the OwsManifest metadata containing the project information and file hashes.
    /// </summary>
    /// <returns>The populated <see cref="OwsManifest"/> object.</returns>
    /// <param name="projectRootPath">The root directory path of the project.</param>
    /// <param name="timelineText">The raw timeline log contents.</param>
    /// <param name="versionGraphText">The raw version graph contents.</param>
    /// <param name="artifactHashes">A dictionary of internal zip artifact paths mapping to their hashes.</param>
    /// <param name="hashService">The hash service used to compute SHA-256 digests.</param>
    public static OwsManifest BuildManifest(
        string projectRootPath,
        string timelineText,
        string versionGraphText,
        Dictionary<string, string> artifactHashes,
        Sha256HashService hashService
    ) {
        return new OwsManifest {
            ProjectName = Path.GetFileName(projectRootPath),
            Platform = Environment.OSVersion.Platform.ToString(),
            TrackedPath = projectRootPath,
            TimelineHash = hashService.ComputeHash(timelineText),
            VersionGraphHash = hashService.ComputeHash(versionGraphText),
            ArtifactHashes = artifactHashes
        };
    }
}
