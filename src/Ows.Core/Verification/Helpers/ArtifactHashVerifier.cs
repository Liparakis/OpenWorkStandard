using System.IO.Compression;
using Ows.Core.Hashing;
using Ows.Core.Packaging;

namespace Ows.Core.Verification;

/// <summary>
/// Verification helper for validating integrity of archive entry hashes against manifest declarations.
/// </summary>
internal static class ArtifactHashVerifier {
    /// <summary>
    /// Validates the SHA-256 hashes of standard files, session states, and custom artifacts in the zip archive against manifest entries.
    /// </summary>
    /// <param name="archive">The ZIP archive container of the package.</param>
    /// <param name="manifest">The manifest object declaring expected hashes.</param>
    /// <param name="errors">The list of error messages to accumulate verification failures.</param>
    public static void ValidateHashes(ZipArchive archive, OwsManifest manifest, List<string> errors) {
        var hashService = new Sha256HashService();

        var timelineEntry = archive.GetEntry(OwsConstants.TimelineFileName);
        if (timelineEntry is null) {
            errors.Add($"Missing timeline entry in package: {OwsConstants.TimelineFileName}");
        } else {
            using var timelineReader = new StreamReader(timelineEntry.Open());
            var timelineText = timelineReader.ReadToEnd();
            var actualTimelineHash = hashService.ComputeHash(timelineText);

            if (!string.Equals(actualTimelineHash, manifest.TimelineHash, StringComparison.OrdinalIgnoreCase)) {
                errors.Add("Timeline hash does not match manifest.");
            }
        }

        var graphEntry = archive.GetEntry(OwsConstants.VersionGraphFileName);
        if (graphEntry is null) {
            errors.Add($"Missing version graph entry in package: {OwsConstants.VersionGraphFileName}");
        } else {
            using var graphReader = new StreamReader(graphEntry.Open());
            var graphText = graphReader.ReadToEnd();
            var actualGraphHash = hashService.ComputeHash(graphText);

            if (!string.Equals(actualGraphHash, manifest.VersionGraphHash, StringComparison.OrdinalIgnoreCase)) {
                errors.Add("Version graph hash does not match manifest.");
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.SessionStateHash)) {
            var sessionEntry = archive.GetEntry(OwsConstants.SessionFileName);
            if (sessionEntry is null) {
                errors.Add($"Missing session state entry declared in manifest: {OwsConstants.SessionFileName}");
            } else {
                using var sessionReader = new StreamReader(sessionEntry.Open());
                var sessionText = sessionReader.ReadToEnd();
                var actualSessionHash = hashService.ComputeHash(sessionText);

                if (!string.Equals(actualSessionHash, manifest.SessionStateHash, StringComparison.OrdinalIgnoreCase)) {
                    errors.Add("Session state hash does not match manifest.");
                }
            }
        }

        var declaredArtifactPaths = manifest.ArtifactHashes.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var artifactEntry in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith("artifacts/", StringComparison.Ordinal))) {
            if (!declaredArtifactPaths.Contains(artifactEntry.FullName)) {
                errors.Add($"Unexpected artifact entry not declared in manifest: {artifactEntry.FullName}");
            }
        }

        foreach (var artifact in manifest.ArtifactHashes) {
            var artifactEntry = archive.GetEntry(artifact.Key);

            if (artifactEntry is null) {
                errors.Add($"Missing artifact entry declared in manifest: {artifact.Key}");
                continue;
            }

            using var artifactStream = artifactEntry.Open();
            using var memoryStream = new MemoryStream();
            artifactStream.CopyTo(memoryStream);
            var actualArtifactHash = hashService.ComputeHash(memoryStream.ToArray());

            if (!string.Equals(actualArtifactHash, artifact.Value, StringComparison.OrdinalIgnoreCase)) {
                errors.Add($"Artifact hash does not match manifest: {artifact.Key}");
            }
        }
    }
}
