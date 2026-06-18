using System.IO.Compression;
using System.Text.Json;

using Ows.Core.Events;
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
            ValidateManifest(archive, errors);
            ValidateTimeline(archive, errors);
        }

        return Task.FromResult(
            errors.Count == 0
                ? VerificationResult.Success("OWS verify succeeded.")
                : VerificationResult.Failure("OWS verify failed.", errors));
    }

    private static void ValidateManifest(ZipArchive archive, List<string> errors)
    {
        using var reader = new StreamReader(archive.GetEntry(OwsConstants.ManifestFileName)!.Open());
        var manifestText = reader.ReadToEnd();

        try
        {
            _ = JsonSerializer.Deserialize<OwsManifest>(manifestText)
                ?? throw new JsonException("Manifest deserialized to null.");
        }
        catch (JsonException)
        {
            errors.Add($"Invalid JSON in {OwsConstants.ManifestFileName}");
        }
    }

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
}
