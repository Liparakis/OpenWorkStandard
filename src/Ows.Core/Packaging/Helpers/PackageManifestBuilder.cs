using System;
using System.Collections.Generic;
using System.IO;
using Ows.Core.Hashing;

namespace Ows.Core.Packaging;

internal static class PackageManifestBuilder {
    public static OwsManifest BuildManifest(
        string projectRootPath,
        string timelineText,
        string versionGraphText,
        Dictionary<string, string> artifactHashes,
        Sha256HashService hashService) {
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
