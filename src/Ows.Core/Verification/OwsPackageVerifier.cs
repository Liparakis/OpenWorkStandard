using System.IO.Compression;
using System.Text.Json;

using Ows.Core.Events;
using Ows.Core.Graph;
using Ows.Core.Hashing;
using Ows.Core.Packaging;
using Ows.Core.Verification;

namespace Ows.Core.Verification;

/// <summary>
/// Provides the initial package verification skeleton.
/// </summary>
public sealed class OwsPackageVerifier : IPackageVerifier
{
    /// <inheritdoc />
    public Task<VerificationResult> VerifyAsync(PackageVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<string>();

        if (!File.Exists(request.PackagePath))
        {
            errors.Add($"Package file not found: {request.PackagePath}");
            return Task.FromResult(VerificationResult.Failure("OWS verify failed.", errors));
        }

        using var archive = ZipFile.OpenRead(request.PackagePath);
        var requiredEntries = new[]
        {
            OwsConstants.ManifestFileName,
            OwsConstants.TimelineFileName,
            OwsConstants.VersionGraphFileName
        };

        foreach (var entryName in requiredEntries)
        {
            if (archive.GetEntry(entryName) is null)
            {
                errors.Add($"Missing required entry: {entryName}");
            }
        }

        if (errors.Count == 0)
        {
            var manifest = ValidateManifest(archive, errors);
            ValidateTimeline(archive, errors);
            ValidateVersionGraph(archive, errors);

            if (manifest is not null)
            {
                ValidateHashes(archive, manifest, errors);
            }
        }

        return Task.FromResult(
            errors.Count == 0
                ? VerificationResult.Success("OWS verify succeeded.")
                : VerificationResult.Failure("OWS verify failed.", errors));
    }

    /// <summary>
    /// Validates and deserializes the package manifest entry.
    /// </summary>
    /// <param name="archive">The package archive being verified.</param>
    /// <param name="errors">The mutable verification error collection.</param>
    /// <returns>The deserialized manifest when valid; otherwise <see langword="null"/>.</returns>
    private static OwsManifest? ValidateManifest(ZipArchive archive, List<string> errors)
    {
        using var reader = new StreamReader(archive.GetEntry(OwsConstants.ManifestFileName)!.Open());
        var manifestText = reader.ReadToEnd();

        try
        {
            return JsonSerializer.Deserialize<OwsManifest>(manifestText)
                ?? throw new JsonException("Manifest deserialized to null.");
        }
        catch (JsonException)
        {
            errors.Add($"Invalid JSON in {OwsConstants.ManifestFileName}");
            return null;
        }
    }

    /// <summary>
    /// Validates that each non-empty timeline line is valid event JSON.
    /// </summary>
    /// <param name="archive">The package archive being verified.</param>
    /// <param name="errors">The mutable verification error collection.</param>
    private static void ValidateTimeline(ZipArchive archive, List<string> errors)
    {
        using var reader = new StreamReader(archive.GetEntry(OwsConstants.TimelineFileName)!.Open());
        var lineNumber = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                _ = JsonSerializer.Deserialize<OwsEvent>(line)
                    ?? throw new JsonException("Timeline event deserialized to null.");
            }
            catch (JsonException)
            {
                errors.Add($"Invalid JSON in {OwsConstants.TimelineFileName} at line {lineNumber}");
                return;
            }
        }
    }

    /// <summary>
    /// Validates that the packaged version graph entry is valid JSON.
    /// </summary>
    /// <param name="archive">The package archive being verified.</param>
    /// <param name="errors">The mutable verification error collection.</param>
    private static void ValidateVersionGraph(ZipArchive archive, List<string> errors)
    {
        using var reader = new StreamReader(archive.GetEntry(OwsConstants.VersionGraphFileName)!.Open());
        var graphText = reader.ReadToEnd();

        try
        {
            _ = JsonSerializer.Deserialize<WorkVersionGraph>(graphText)
                ?? throw new JsonException("Version graph deserialized to null.");
        }
        catch (JsonException)
        {
            errors.Add($"Invalid JSON in {OwsConstants.VersionGraphFileName}");
        }
    }

    /// <summary>
    /// Validates manifest hashes for timeline, version graph, and packaged artifacts.
    /// </summary>
    /// <param name="archive">The package archive being verified.</param>
    /// <param name="manifest">The manifest declaring expected hashes.</param>
    /// <param name="errors">The mutable verification error collection.</param>
    private static void ValidateHashes(ZipArchive archive, OwsManifest manifest, List<string> errors)
    {
        var hashService = new Sha256HashService();

        using (var timelineReader = new StreamReader(archive.GetEntry(OwsConstants.TimelineFileName)!.Open()))
        {
            var timelineText = timelineReader.ReadToEnd();
            var actualTimelineHash = hashService.ComputeHash(timelineText);

            if (!string.Equals(actualTimelineHash, manifest.TimelineHash, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Timeline hash does not match manifest.");
            }
        }

        using (var graphReader = new StreamReader(archive.GetEntry(OwsConstants.VersionGraphFileName)!.Open()))
        {
            var graphText = graphReader.ReadToEnd();
            var actualGraphHash = hashService.ComputeHash(graphText);

            if (!string.Equals(actualGraphHash, manifest.VersionGraphHash, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Version graph hash does not match manifest.");
            }
        }

        var declaredArtifactPaths = manifest.ArtifactHashes.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var artifactEntry in archive.Entries.Where(entry => entry.FullName.StartsWith("artifacts/", StringComparison.Ordinal)))
        {
            if (!declaredArtifactPaths.Contains(artifactEntry.FullName))
            {
                errors.Add($"Unexpected artifact entry not declared in manifest: {artifactEntry.FullName}");
            }
        }

        foreach (var artifact in manifest.ArtifactHashes)
        {
            var artifactEntry = archive.GetEntry(artifact.Key);

            if (artifactEntry is null)
            {
                errors.Add($"Missing artifact entry declared in manifest: {artifact.Key}");
                continue;
            }

            using var artifactStream = artifactEntry.Open();
            using var memoryStream = new MemoryStream();
            artifactStream.CopyTo(memoryStream);
            var actualArtifactHash = hashService.ComputeHash(memoryStream.ToArray());

            if (!string.Equals(actualArtifactHash, artifact.Value, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Artifact hash does not match manifest: {artifact.Key}");
            }
        }
    }
}
