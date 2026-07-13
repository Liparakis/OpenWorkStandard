using System.IO.Compression;
using System.Text.Json;

namespace Ows.Core.Packaging.Helpers;

/// <summary>
///     Represents the <see cref="PackageArchiveWriter" /> type.
/// </summary>
internal static class PackageArchiveWriter {
    /// <summary>
    ///     Creates a new zip archive containing the OWS package components.
    /// </summary>
    /// <param name="outputPackagePath">The file path where the package zip file will be written.</param>
    /// <param name="projectRootPath">The root directory of the project.</param>
    /// <param name="manifest">The package manifest.</param>
    /// <param name="timelineText">The raw timeline log content.</param>
    /// <param name="versionGraphText">The raw version graph content.</param>
    /// <param name="artifactHashes">A dictionary mapping internal zip artifact paths to their hashes.</param>
    /// <param name="signature">The optional package signature metadata.</param>
    public static void WriteArchive(
        string outputPackagePath,
        string projectRootPath,
        OwsManifest manifest,
        string timelineText,
        string versionGraphText,
        Dictionary<string, string> artifactHashes,
        OwsPackageSignature? signature
    ) {
        if (File.Exists(outputPackagePath)) {
            File.Delete(outputPackagePath);
        }

        using var archive = ZipFile.Open(outputPackagePath, ZipArchiveMode.Create);

        var manifestEntry = archive.CreateEntry(OwsConstants.ManifestFileName);
        using (var writer = new StreamWriter(manifestEntry.Open())) {
            writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }

        var timelineEntry = archive.CreateEntry(OwsConstants.TimelineFileName);
        using (var timelineWriter = new StreamWriter(timelineEntry.Open())) {
            timelineWriter.Write(timelineText);
        }

        var graphEntry = archive.CreateEntry(OwsConstants.VersionGraphFileName);
        using (var graphWriter = new StreamWriter(graphEntry.Open())) {
            graphWriter.Write(versionGraphText);
        }

        if (signature is not null) {
            var signatureEntry = archive.CreateEntry(OwsConstants.SignatureFileName);
            using var signatureWriter = new StreamWriter(signatureEntry.Open());
            signatureWriter.Write(
                JsonSerializer.Serialize(signature, new JsonSerializerOptions { WriteIndented = true })
            );
        }

        foreach (var artifactPath in artifactHashes.Keys) {
            var relativePath = artifactPath["artifacts/".Length..].Replace('/', Path.DirectorySeparatorChar);
            archive.CreateEntryFromFile(Path.Combine(projectRootPath, relativePath), artifactPath);
        }
    }
}
