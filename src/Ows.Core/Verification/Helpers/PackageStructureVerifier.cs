using System.IO.Compression;
using System.Text.Json;
using Ows.Core.Graph;
using Ows.Core.Packaging;

namespace Ows.Core.Verification.Helpers;

/// <summary>
/// Verification helper for validating the presence and JSON validity of required files inside an OWS package.
/// </summary>
internal static class PackageStructureVerifier {
    /// <summary>
    /// The array of file entry paths required to exist in every valid OWS package structure.
    /// </summary>
    private static readonly string[] RequiredEntries = [
        OwsConstants.ManifestFileName,
        OwsConstants.TimelineFileName,
        OwsConstants.VersionGraphFileName
    ];

    /// <summary>
    /// Checks if all required file entries are present in the ZIP archive.
    /// </summary>
    /// <param name="archive">The target ZIP package.</param>
    /// <param name="errors">The list to accumulate verification errors.</param>
    public static void VerifyRequiredEntries(ZipArchive archive, List<string> errors) {
        foreach (var entryName in RequiredEntries) {
            if (archive.GetEntry(entryName) is null) {
                errors.Add($"Missing required entry: {entryName}");
            }
        }
    }

    /// <summary>
    /// Extracts and parses the <see cref="OwsManifest"/> from the package ZIP archive.
    /// </summary>
    /// <param name="archive">The target ZIP package.</param>
    /// <param name="errors">The list to accumulate verification errors.</param>
    /// <returns>The parsed manifest instance, or <see langword="null"/> if extraction or deserialization fails.</returns>
    public static OwsManifest? ValidateManifest(ZipArchive archive, List<string> errors) {
        var entry = archive.GetEntry(OwsConstants.ManifestFileName);
        if (entry is null) return null;

        try {
            using var reader = new StreamReader(entry.Open());
            var manifestText = reader.ReadToEnd();
            return JsonSerializer.Deserialize<OwsManifest>(manifestText)
                   ?? throw new JsonException("Manifest deserialized to null.");
        } catch (JsonException) {
            errors.Add($"Invalid JSON in {OwsConstants.ManifestFileName}");
            return null;
        }
    }

    /// <summary>
    /// Validates that the version graph JSON file inside the ZIP archive compiles to a valid <see cref="WorkVersionGraph"/> structure.
    /// </summary>
    /// <param name="archive">The target ZIP package.</param>
    /// <param name="errors">The list to accumulate verification errors.</param>
    public static void ValidateVersionGraph(ZipArchive archive, List<string> errors) {
        var entry = archive.GetEntry(OwsConstants.VersionGraphFileName);
        if (entry is null) return;

        try {
            using var reader = new StreamReader(entry.Open());
            var graphText = reader.ReadToEnd();
            _ = JsonSerializer.Deserialize<WorkVersionGraph>(graphText)
                ?? throw new JsonException("Version graph deserialized to null.");
        } catch (JsonException) {
            errors.Add($"Invalid JSON in {OwsConstants.VersionGraphFileName}");
        }
    }
}
