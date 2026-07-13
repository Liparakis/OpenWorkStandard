using System.Text;
using System.Text.Json;
using Ows.Core.Hashing;

namespace Ows.Core.Packaging;

/// <summary>
///     Represents the <see cref="PackageRootCanonicalizer" /> type.
/// </summary>
internal static class PackageRootCanonicalizer {
    /// <summary>
    ///     The OWS package root format identifier string.
    /// </summary>
    private const string Format = "OWS-PACKAGE-ROOT-V1";

    /// <summary>
    ///     Formats a package manifest and its contents into a canonical byte representation for hashing and signing.
    /// </summary>
    /// <returns>A byte array containing the UTF-8 encoded canonical package representation.</returns>
    /// <param name="manifest">The package manifest to serialize canonically.</param>
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
            $"version_graph={manifest.VersionGraphHash}"
        };
        lines.AddRange(
            manifest.ArtifactHashes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"artifact={pair.Key}={pair.Value}")
        );
        return Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
    }

    /// <summary>
    ///     Computes the SHA-256 hash of the canonical package representation.
    /// </summary>
    /// <returns>The hexadecimal SHA-256 digest of the canonical package.</returns>
    /// <param name="manifest">The package manifest to hash.</param>
    public static string ComputeHash(OwsManifest manifest) {
        return Sha256HashService.ComputeHash(BuildCanonicalBytes(manifest));
    }
}
