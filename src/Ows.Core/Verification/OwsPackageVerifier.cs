using System.IO.Compression;
using System.Text.Json;
using Ows.Core.Events;
using Ows.Core.Graph;
using Ows.Core.Hashing;
using Ows.Core.Notarization;
using Ows.Core.Packaging;

namespace Ows.Core.Verification;

/// <summary>
/// Provides the initial package verification skeleton.
/// </summary>
public sealed class OwsPackageVerifier : IPackageVerifier
{
    private static readonly VerificationFinding MissingRemoteReceiptsFinding = new()
    {
        Code = "remote-receipts-missing",
        Title = "Remote receipts missing",
        Detail = "The package is locally consistent, but no remote verifier receipts were provided."
    };

    private static readonly VerificationFinding RemoteReceiptsNotPackagedFinding = new()
    {
        Code = "remote-receipts-not-packaged",
        Title = "Remote receipts not packaged",
        Detail = "The live verifier returned matching receipts, but the package did not include them."
    };

    /// <inheritdoc />
    public Task<VerificationResult> VerifyAsync(PackageVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<string>();
        var findings = new List<VerificationFinding>();

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
            var timelineHeadHash = ValidateTimeline(archive, errors);
            ValidateVersionGraph(archive, errors);

            if (manifest is not null)
            {
                ValidateHashes(archive, manifest, errors);

                if (errors.Count == 0)
                {
                    var trustStatus = ValidateReceipts(
                        archive,
                        timelineHeadHash,
                        request.TrustedReceiptChain,
                        request.TrustedSessionHead,
                        errors,
                        findings);
                    return Task.FromResult(
                        errors.Count == 0
                            ? VerificationResult.Success("OWS verify succeeded.", trustStatus, findings)
                            : VerificationResult.Failure("OWS verify failed.", errors));
                }
            }
        }

        return Task.FromResult(
            VerificationResult.Failure("OWS verify failed.", errors));
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
    /// Validates that each non-empty timeline line is valid event JSON and preserves the event hash chain.
    /// </summary>
    /// <param name="archive">The package archive being verified.</param>
    /// <param name="errors">The mutable verification error collection.</param>
    private static string ValidateTimeline(ZipArchive archive, List<string> errors)
    {
        using var reader = new StreamReader(archive.GetEntry(OwsConstants.TimelineFileName)!.Open());
        var lineNumber = 0;
        var expectedPreviousHash = OwsEventChain.GenesisPreviousEventHash;
        var lastEventHash = OwsEventChain.GenesisPreviousEventHash;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var owsEvent = JsonSerializer.Deserialize<OwsEvent>(line)
                               ?? throw new JsonException("Timeline event deserialized to null.");

                if (!string.Equals(owsEvent.PreviousEventHash, expectedPreviousHash, StringComparison.Ordinal))
                {
                    errors.Add($"Broken event chain in {OwsConstants.TimelineFileName} at line {lineNumber}");
                    return OwsEventChain.GenesisPreviousEventHash;
                }

                var actualEventHash = OwsEventChain.ComputeEventHash(owsEvent);
                if (!string.Equals(owsEvent.EventHash, actualEventHash, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Invalid event hash in {OwsConstants.TimelineFileName} at line {lineNumber}");
                    return OwsEventChain.GenesisPreviousEventHash;
                }

                expectedPreviousHash = owsEvent.EventHash;
                lastEventHash = owsEvent.EventHash;
            }
            catch (JsonException)
            {
                errors.Add($"Invalid JSON in {OwsConstants.TimelineFileName} at line {lineNumber}");
                return OwsEventChain.GenesisPreviousEventHash;
            }
        }

        return lastEventHash;
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

        if (!string.IsNullOrWhiteSpace(manifest.SessionStateHash))
        {
            var sessionEntry = archive.GetEntry(OwsConstants.SessionFileName);
            if (sessionEntry is null)
            {
                errors.Add($"Missing session state entry declared in manifest: {OwsConstants.SessionFileName}");
            }
            else
            {
                using var sessionReader = new StreamReader(sessionEntry.Open());
                var sessionText = sessionReader.ReadToEnd();
                var actualSessionHash = hashService.ComputeHash(sessionText);

                if (!string.Equals(actualSessionHash, manifest.SessionStateHash, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("Session state hash does not match manifest.");
                }
            }
        }

        var declaredArtifactPaths = manifest.ArtifactHashes.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var artifactEntry in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith("artifacts/", StringComparison.Ordinal)))
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

    /// <summary>
    /// Validates optional packaged receipts and derives the resulting trust grade.
    /// </summary>
    /// <param name="archive">The package archive being verified.</param>
    /// <param name="timelineHeadHash">The verified local timeline head hash.</param>
    /// <param name="trustedReceiptChain">The optional authoritative receipt chain fetched from a live verifier.</param>
    /// <param name="trustedSessionHead">The optional authoritative session head fetched from a live verifier.</param>
    /// <param name="errors">The mutable verification error collection.</param>
    /// <param name="findings">The mutable verification findings collection.</param>
    /// <returns>The resulting trust grade.</returns>
    private static TrustStatus ValidateReceipts(
        ZipArchive archive,
        string timelineHeadHash,
        ReceiptChain? trustedReceiptChain,
        SessionHeadResponse? trustedSessionHead,
        List<string> errors,
        List<VerificationFinding> findings)
    {
        var packagedReceiptChain = ReadPackagedReceiptChain(archive, errors);
        if (errors.Count > 0)
        {
            return TrustStatus.Invalid;
        }

        if (trustedReceiptChain is not null)
        {
            if (!ReceiptChainVerifier.IsValid(trustedReceiptChain))
            {
                errors.Add("Trusted remote receipt chain is invalid.");
                return TrustStatus.Invalid;
            }

            if (packagedReceiptChain is null)
            {
                var trustedLastReceipt = trustedReceiptChain.Receipts.LastOrDefault();
                if (trustedLastReceipt is null)
                {
                    findings.Add(MissingRemoteReceiptsFinding);
                    return TrustStatus.Unverified;
                }

                if (!string.Equals(trustedLastReceipt.TimelineHeadHash, timelineHeadHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("Trusted remote receipt chain head does not match the local timeline head.");
                    return TrustStatus.Invalid;
                }

                findings.Add(RemoteReceiptsNotPackagedFinding);
                return TrustStatus.Unverified;
            }

            if (!AreEquivalentReceiptChains(packagedReceiptChain, trustedReceiptChain))
            {
                errors.Add("Packaged receipt chain does not match the trusted remote receipt chain.");
                return TrustStatus.Invalid;
            }
        }
        else if (trustedSessionHead is not null)
        {
            if (string.IsNullOrWhiteSpace(trustedSessionHead.SessionId))
            {
                errors.Add("Trusted remote session head is invalid.");
                return TrustStatus.Invalid;
            }

            if (packagedReceiptChain is null)
            {
                if (!string.Equals(trustedSessionHead.LastTimelineHeadHash, timelineHeadHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("Trusted remote session head does not match the local timeline head.");
                    return TrustStatus.Invalid;
                }

                findings.Add(RemoteReceiptsNotPackagedFinding);
                return TrustStatus.Unverified;
            }

            var packagedLastReceipt = packagedReceiptChain.Receipts.LastOrDefault();
            if (packagedLastReceipt is null)
            {
                findings.Add(MissingRemoteReceiptsFinding);
                return TrustStatus.Unverified;
            }

            if (packagedLastReceipt.SequenceNumber != trustedSessionHead.LastSequenceNumber ||
                !string.Equals(packagedLastReceipt.TimelineHeadHash, trustedSessionHead.LastTimelineHeadHash,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(packagedLastReceipt.ReceiptHash, trustedSessionHead.LastReceiptHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Packaged receipt head does not match the trusted remote session head.");
                return TrustStatus.Invalid;
            }
        }

        if (packagedReceiptChain is null)
        {
            findings.Add(MissingRemoteReceiptsFinding);
            return TrustStatus.Unverified;
        }

        if (!ReceiptChainVerifier.IsValid(packagedReceiptChain))
        {
            errors.Add("Receipt chain is invalid.");
            return TrustStatus.Invalid;
        }

        var lastReceipt = packagedReceiptChain.Receipts.LastOrDefault();
        if (lastReceipt is null)
        {
            findings.Add(MissingRemoteReceiptsFinding);
            return TrustStatus.Unverified;
        }

        if (!string.Equals(lastReceipt.TimelineHeadHash, timelineHeadHash, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Receipt chain head does not match the local timeline head.");
            return TrustStatus.Invalid;
        }

        return TrustStatus.Verified;
    }

    /// <summary>
    /// Reads and deserializes the packaged receipt chain when present.
    /// </summary>
    /// <param name="archive">The package archive being verified.</param>
    /// <param name="errors">The mutable verification error collection.</param>
    /// <returns>The deserialized receipt chain when present and valid JSON; otherwise <see langword="null"/>.</returns>
    private static ReceiptChain? ReadPackagedReceiptChain(ZipArchive archive, List<string> errors)
    {
        var receiptsEntry = archive.GetEntry(OwsConstants.ReceiptsFileName);
        if (receiptsEntry is null)
        {
            return null;
        }

        using var reader = new StreamReader(receiptsEntry.Open());
        var receiptsText = reader.ReadToEnd();

        try
        {
            return JsonSerializer.Deserialize<ReceiptChain>(receiptsText)
                   ?? throw new JsonException("Receipt chain deserialized to null.");
        }
        catch (JsonException)
        {
            errors.Add($"Invalid JSON in {OwsConstants.ReceiptsFileName}");
            return null;
        }
    }

    /// <summary>
    /// Compares two receipt chains for exact session and ordered-receipt equality.
    /// </summary>
    /// <param name="left">The first receipt chain.</param>
    /// <param name="right">The second receipt chain.</param>
    /// <returns><see langword="true"/> when both chains match exactly; otherwise <see langword="false"/>.</returns>
    private static bool AreEquivalentReceiptChains(ReceiptChain left, ReceiptChain right) =>
        left.SessionId == right.SessionId &&
        left.Receipts.SequenceEqual(right.Receipts);
}