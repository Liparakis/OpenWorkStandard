using System.Text;
using System.Text.Json;
using Ows.Core.Hashing;

namespace Ows.Core.Packaging;

internal static class PackageRootCanonicalizer {
    private const string Format = "OWS-PACKAGE-ROOT-V1";

    public static byte[] BuildCanonicalBytes(OwsManifest manifest) {
        var canonicalManifest = manifest with {
            PackageId = string.Empty,
            GeneratedAtUtc = default,
            PackageRootHash = string.Empty,
            SignatureAlgorithm = string.Empty,
            SignatureKeyFingerprint = string.Empty,
            ArtifactHashes = new SortedDictionary<string, string>(
                manifest.ArtifactHashes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                StringComparer.Ordinal
            )
        };
        var manifestJson = JsonSerializer.Serialize(
            canonicalManifest, new JsonSerializerOptions {
                WriteIndented = false
            }
        );
        var lines = new List<string> {
            Format,
            $"manifest={new Sha256HashService().ComputeHash(manifestJson)}",
            $"timeline={manifest.TimelineHash}",
            $"version_graph={manifest.VersionGraphHash}",
        };
        lines.AddRange(
            manifest.ArtifactHashes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"artifact={pair.Key}={pair.Value}")
        );
        return Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
    }

    public static string ComputeHash(OwsManifest manifest) =>
        Sha256HashService.ComputeHash(BuildCanonicalBytes(manifest));
}
