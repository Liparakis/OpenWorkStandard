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
    private static readonly VerificationFinding TimelineChainValidFinding = new()
    {
        Code = "timeline.chain.valid",
        Severity = "Low",
        Title = "Timeline chain valid",
        Detail = "The local timeline event chain is complete and unbroken.",
        TechnicalDetail = "All parent-child event hashes match and form a single continuous timeline.",
        ReviewerAction = "No action required."
    };

    private static readonly VerificationFinding TimelineChainBrokenFinding = new()
    {
        Code = "timeline.chain.broken",
        Severity = "Critical",
        Title = "Timeline chain broken",
        Detail = "The local timeline event chain is broken or inconsistent.",
        TechnicalDetail = "A parent event hash mismatch was detected, or events were reordered or modified.",
        ReviewerAction = "Request a resubmission. The evidence chain is incomplete."
    };

    private static readonly VerificationFinding ReceiptChainValidFinding = new()
    {
        Code = "receipt.chain.valid",
        Severity = "Low",
        Title = "Receipt chain valid",
        Detail = "The package contains valid remote notarization receipts.",
        TechnicalDetail = "All checkpoints are signed and align with the local timeline.",
        ReviewerAction = "No action required."
    };

    private static readonly VerificationFinding ReceiptChainMissingFinding = new()
    {
        Code = "receipt.chain.missing",
        Severity = "Medium",
        Title = "Receipt chain missing",
        Detail = "The package does not contain verifier receipts.",
        TechnicalDetail = "No receipts were found in receipts.json, or remote verifier did not return receipts.",
        ReviewerAction = "Verify whether this package was intended to run in local-only mode. If remote verification is expected, request a resubmission."
    };

    private static readonly VerificationFinding LeaseGapShortFinding = new()
    {
        Code = "lease.gap.short",
        Severity = "Warning",
        Title = "Short session continuity gap",
        Detail = "Session heartbeat was briefly interrupted, but session continuity was mostly preserved.",
        TechnicalDetail = "Session lease expired with a max gap duration less than or equal to the significance threshold.",
        ReviewerAction = "Review file changes around this interval. Session continuity could not be verified."
    };

    private static readonly VerificationFinding LeaseGapLongFinding = new()
    {
        Code = "lease.gap.long",
        Severity = "High",
        Title = "Significant session continuity gap",
        Detail = "Session heartbeat was interrupted.",
        TechnicalDetail = "Session lease expired with a max gap duration exceeding the significance threshold.",
        ReviewerAction = "Review file changes around this interval. Session continuity could not be verified."
    };

    private static readonly VerificationFinding PackageAnchorValidFinding = new()
    {
        Code = "package.anchor.valid",
        Severity = "Low",
        Title = "Package anchor valid",
        Detail = "The package is anchored to the registered verifier session head.",
        TechnicalDetail = "Timeline head matches the verifier session head.",
        ReviewerAction = "No action required."
    };

    private static readonly VerificationFinding PackageAnchorMissingFinding = new()
    {
        Code = "package.anchor.missing",
        Severity = "Medium",
        Title = "Package anchor missing",
        Detail = "The package is not anchored to a registered verifier session head.",
        TechnicalDetail = "The verifier session was not found or has no matching anchor.",
        ReviewerAction = "Ensure that the session was synchronized with the verifier."
    };

    private static readonly VerificationFinding PackageHashInvalidFinding = new()
    {
        Code = "package.hash.invalid",
        Severity = "Critical",
        Title = "Package hash invalid",
        Detail = "Package files have modified hashes that do not match the manifest.",
        TechnicalDetail = "SHA-256 hash of one or more files in the package does not match the manifest value.",
        ReviewerAction = "Reject the package as corrupted or modified. Request a resubmission."
    };

    private static readonly VerificationFinding VerifierSessionHeadMismatchFinding = new()
    {
        Code = "verifier.session.head.mismatch",
        Severity = "High",
        Title = "Verifier session head mismatch",
        Detail = "The session head reported by the verifier does not match the local timeline head.",
        TechnicalDetail = "Mismatch between trusted remote session head hash and local package timeline head hash.",
        ReviewerAction = "Manual review recommended. Inspect timeline synchronization logs."
    };

    private static readonly VerificationFinding LeaseWorkAfterExpirationFinding = new()
    {
        Code = "lease.work_after_expiration",
        Severity = "High",
        Title = "Work after lease expiration",
        Detail = "Timeline events were recorded after the remote verifier session lease expired.",
        TechnicalDetail = "Local timeline events have timestamps after the lease expiration timestamp.",
        ReviewerAction = "Examine work recorded after lease expiration. Session continuity could not be verified."
    };

    /// <inheritdoc />
    public Task<VerificationResult> VerifyAsync(PackageVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<string>();
        var findings = new List<VerificationFinding>();
        var generatedAt = DateTimeOffset.UtcNow.ToString("o");

        if (!File.Exists(request.PackagePath))
        {
            errors.Add($"Package file not found: {request.PackagePath}");
            return Task.FromResult(new VerificationResult
            {
                IsSuccess = false,
                TrustStatus = TrustStatus.Invalid,
                Summary = "OWS verify failed.",
                Errors = errors,
                GeneratedAt = generatedAt,
                Recommendation = "Reject as invalid package / request resubmission",
                TrustExplanation = "The package, timeline, receipt chain, or hashes are broken or inconsistent.",
                Education = request.EducationContext
            });
        }

        string packageHash = string.Empty;
        try
        {
            var packageBytes = File.ReadAllBytes(request.PackagePath);
            packageHash = new Sha256HashService().ComputeHash(packageBytes);
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to compute package hash: {ex.Message}");
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

        OwsManifest? manifest = null;
        string? timelineHeadHash = null;
        var eventTimestamps = new List<DateTimeOffset>();

        if (archive.GetEntry(OwsConstants.ManifestFileName) is not null)
        {
            manifest = ValidateManifest(archive, errors);
        }

        if (archive.GetEntry(OwsConstants.TimelineFileName) is not null)
        {
            timelineHeadHash = ValidateTimeline(archive, errors, out eventTimestamps);
        }

        if (archive.GetEntry(OwsConstants.VersionGraphFileName) is not null)
        {
            ValidateVersionGraph(archive, errors);
        }

        // Timeline Integrity Finding
        if (string.IsNullOrEmpty(timelineHeadHash) || errors.Any(e => e.Contains("timeline", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(TimelineChainBrokenFinding);
        }
        else
        {
            findings.Add(TimelineChainValidFinding);
        }

        // Hash Validation Finding
        if (manifest is not null)
        {
            var initialErrorCount = errors.Count;
            ValidateHashes(archive, manifest, errors);
            if (errors.Count > initialErrorCount)
            {
                findings.Add(PackageHashInvalidFinding);
            }
        }

        var trustStatus = TrustStatus.Invalid;
        var verifiedKeyFingerprints = new List<string>();

        if (errors.Count == 0 && manifest is not null)
        {
            trustStatus = ValidateReceipts(
                archive,
                timelineHeadHash!,
                request.TrustedReceiptChain,
                request.TrustedSessionHead,
                errors,
                findings,
                out verifiedKeyFingerprints);
        }

        // Session Lease Continuity
        var leaseStatus = "None";
        var gaps = new List<ReportLeaseGapInfo>();

        if (errors.Count == 0 && trustStatus != TrustStatus.Invalid)
        {
            if (request.SessionHasLeaseGap)
            {
                if (request.SessionMaxLeaseGapSeconds > request.SignificantGapSeconds)
                {
                    findings.Add(LeaseGapLongFinding);
                    leaseStatus = "Unverified";
                    if (trustStatus == TrustStatus.Verified || trustStatus == TrustStatus.Degraded)
                    {
                        trustStatus = TrustStatus.Unverified;
                    }
                }
                else
                {
                    findings.Add(LeaseGapShortFinding);
                    leaseStatus = "Degraded";
                    if (trustStatus == TrustStatus.Verified)
                    {
                        trustStatus = TrustStatus.Degraded;
                    }
                }

                gaps.Add(new ReportLeaseGapInfo
                {
                    StartTime = request.SessionFirstLeaseGapAt ?? DateTimeOffset.MinValue,
                    DurationSeconds = request.SessionMaxLeaseGapSeconds
                });
            }
            else if (request.SessionLastHeartbeatAt.HasValue || request.SessionLeaseExpiresAt.HasValue)
            {
                leaseStatus = "Active";
            }

            if (request.SessionLeaseExpiresAt.HasValue)
            {
                var hasWorkAfterLease = eventTimestamps.Any(t => t > request.SessionLeaseExpiresAt.Value);
                if (hasWorkAfterLease)
                {
                    findings.Add(LeaseWorkAfterExpirationFinding);
                    var exceedDuration = eventTimestamps.Where(t => t > request.SessionLeaseExpiresAt.Value)
                        .Select(t => (t - request.SessionLeaseExpiresAt.Value).TotalSeconds)
                        .DefaultIfEmpty(0)
                        .Max();

                    if (exceedDuration > request.SignificantGapSeconds)
                    {
                        leaseStatus = "Unverified";
                        if (trustStatus == TrustStatus.Verified || trustStatus == TrustStatus.Degraded)
                        {
                            trustStatus = TrustStatus.Unverified;
                        }
                    }
                    else
                    {
                        if (leaseStatus == "Active")
                        {
                            leaseStatus = "Degraded";
                        }
                        if (trustStatus == TrustStatus.Verified)
                        {
                            trustStatus = TrustStatus.Degraded;
                        }
                    }
                }
            }
        }

        // Package Anchor Status
        var anchorStatus = "Missing";
        var anchoredSessionHead = "None";

        if (errors.Count == 0 && manifest is not null)
        {
            var hasRemote = request.TrustedSessionHead is not null || request.TrustedReceiptChain is not null;
            if (hasRemote)
            {
                var expectedHead = request.TrustedSessionHead?.LastTimelineHeadHash ?? request.TrustedReceiptChain?.Receipts.LastOrDefault()?.TimelineHeadHash;
                anchoredSessionHead = expectedHead ?? "None";

                var matches = string.Equals(expectedHead, timelineHeadHash, StringComparison.OrdinalIgnoreCase);
                if (matches)
                {
                    findings.Add(PackageAnchorValidFinding);
                    anchorStatus = "Anchored";
                }
                else
                {
                    findings.Add(VerifierSessionHeadMismatchFinding);
                    findings.Add(PackageAnchorMissingFinding);
                    anchorStatus = "Mismatch";
                }
            }
            else
            {
                findings.Add(PackageAnchorMissingFinding);
                anchorStatus = "Missing";
            }
        }

        // If there are errors, trustStatus is Invalid
        if (errors.Count > 0)
        {
            trustStatus = TrustStatus.Invalid;
        }

        var trustExplanation = trustStatus switch
        {
            TrustStatus.Verified => "The package, local timeline, remote receipts, and verifier session state align.",
            TrustStatus.Degraded => "The submission has minor or bounded continuity issues. The evidence is mostly usable, but manual review is recommended.",
            TrustStatus.Unverified => "The package is structurally valid, but OWS cannot verify enough of the work timeline or remote session continuity.",
            _ => "The package, timeline, receipt chain, or hashes are broken or inconsistent."
        };

        var recommendation = trustStatus switch
        {
            TrustStatus.Verified => "Accept as verified",
            TrustStatus.Degraded => "Manual review recommended",
            TrustStatus.Unverified => "Manual review required",
            _ => "Reject as invalid package / request resubmission"
        };

        // Resolve package info
        var packagedReceiptChain = ReadPackagedReceiptChain(archive, new List<string>());
        var sessionId = ReadSessionId(archive) 
            ?? packagedReceiptChain?.SessionId.Value
            ?? request.TrustedReceiptChain?.SessionId.Value
            ?? request.TrustedSessionHead?.SessionId 
            ?? "Unknown";

        var packageInfo = new ReportPackageInfo
        {
            PackageId = manifest?.PackageId ?? "Unknown",
            PackageHash = packageHash,
            SessionId = sessionId
        };

        var timelineInfo = new ReportTimelineInfo
        {
            Integrity = (string.IsNullOrEmpty(timelineHeadHash) || errors.Any(e => e.Contains("timeline", StringComparison.OrdinalIgnoreCase))) ? "Broken" : "Valid",
            EventCount = eventTimestamps.Count,
            HeadEventHash = timelineHeadHash ?? "None"
        };

        var receiptsAlignment = "Missing";
        if (request.TrustedReceiptChain is not null || packagedReceiptChain is not null)
        {
            var matched = true;
            if (request.TrustedReceiptChain is not null && packagedReceiptChain is not null)
            {
                matched = AreEquivalentReceiptChains(packagedReceiptChain, request.TrustedReceiptChain);
            }
            receiptsAlignment = matched ? "Aligned" : "Misaligned";
        }

        var receiptsInfo = new ReportReceiptsInfo
        {
            Alignment = receiptsAlignment,
            ReceiptCount = packagedReceiptChain?.Receipts.Count ?? request.TrustedReceiptChain?.Receipts.Count ?? 0,
            HeadReceiptHash = packagedReceiptChain?.Receipts.LastOrDefault()?.ReceiptHash ?? request.TrustedReceiptChain?.Receipts.LastOrDefault()?.ReceiptHash ?? "None"
        };

        var leaseInfo = new ReportLeaseInfo
        {
            Status = leaseStatus,
            LastHeartbeatAt = request.SessionLastHeartbeatAt?.ToString("o") ?? "None",
            LeaseExpiresAt = request.SessionLeaseExpiresAt?.ToString("o") ?? "None",
            Gaps = gaps
        };

        var anchoredAt = request.TrustedReceiptChain?.Receipts.LastOrDefault()?.ServerTimestamp.IssuedAtUtc.ToString("o") ?? "None";
        var anchorInfo = new ReportAnchorInfo
        {
            Status = anchorStatus,
            AnchoredAt = anchoredAt,
            AnchoredSessionHead = anchoredSessionHead
        };

        var isSuccess = trustStatus != TrustStatus.Invalid;

        return Task.FromResult(new VerificationResult
        {
            IsSuccess = isSuccess,
            TrustStatus = trustStatus,
            Summary = isSuccess ? "OWS verify succeeded." : "OWS verify failed.",
            Errors = errors,
            Findings = findings,
            VerifiedKeyFingerprints = verifiedKeyFingerprints,
            TrustExplanation = trustExplanation,
            Recommendation = recommendation,
            GeneratedAt = generatedAt,
            Package = packageInfo,
            Timeline = timelineInfo,
            Receipts = receiptsInfo,
            Lease = leaseInfo,
            Anchor = anchorInfo,
            Education = request.EducationContext
        });
    }

    private static string? ReadSessionId(ZipArchive archive)
    {
        var entry = archive.GetEntry(OwsConstants.SessionFileName);
        if (entry is null) return null;
        try
        {
            using var reader = new StreamReader(entry.Open());
            var text = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("SessionId", out var prop))
            {
                return prop.GetString();
            }
            if (doc.RootElement.TryGetProperty("sessionId", out prop))
            {
                return prop.GetString();
            }
        }
        catch
        {
            // Ignore failures
        }
        return null;
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
    /// <param name="verifiedKeyFingerprints">The output list of verified key fingerprints.</param>
    /// <returns>The resulting trust grade.</returns>
    private static TrustStatus ValidateReceipts(
        ZipArchive archive,
        string timelineHeadHash,
        ReceiptChain? trustedReceiptChain,
        SessionHeadResponse? trustedSessionHead,
        List<string> errors,
        List<VerificationFinding> findings,
        out List<string> verifiedKeyFingerprints)
    {
        verifiedKeyFingerprints = new List<string>();
        var packagedReceiptChain = ReadPackagedReceiptChain(archive, errors);
        if (packagedReceiptChain is not null)
        {
            foreach (var receipt in packagedReceiptChain.Receipts)
            {
                if (!string.IsNullOrWhiteSpace(receipt.SigningKeyFingerprint) && !verifiedKeyFingerprints.Contains(receipt.SigningKeyFingerprint))
                {
                    verifiedKeyFingerprints.Add(receipt.SigningKeyFingerprint);
                }
            }
        }
        if (trustedReceiptChain is not null)
        {
            foreach (var receipt in trustedReceiptChain.Receipts)
            {
                if (!string.IsNullOrWhiteSpace(receipt.SigningKeyFingerprint) && !verifiedKeyFingerprints.Contains(receipt.SigningKeyFingerprint))
                {
                    verifiedKeyFingerprints.Add(receipt.SigningKeyFingerprint);
                }
            }
        }
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
                    findings.Add(ReceiptChainMissingFinding);
                    return TrustStatus.Unverified;
                }

                if (!string.Equals(trustedLastReceipt.TimelineHeadHash, timelineHeadHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("Trusted remote receipt chain head does not match the local timeline head.");
                    return TrustStatus.Invalid;
                }

                findings.Add(ReceiptChainMissingFinding);
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

                findings.Add(ReceiptChainMissingFinding);
                return TrustStatus.Unverified;
            }

            var packagedLastReceipt = packagedReceiptChain.Receipts.LastOrDefault();
            if (packagedLastReceipt is null)
            {
                findings.Add(ReceiptChainMissingFinding);
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
            findings.Add(ReceiptChainMissingFinding);
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
            findings.Add(ReceiptChainMissingFinding);
            return TrustStatus.Unverified;
        }

        if (!string.Equals(lastReceipt.TimelineHeadHash, timelineHeadHash, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Receipt chain head does not match the local timeline head.");
            return TrustStatus.Invalid;
        }

        findings.Add(ReceiptChainValidFinding);
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
    /// <param name="eventTimestamps">Output containing all the parsed timeline event timestamps.</param>
    private static string ValidateTimeline(ZipArchive archive, List<string> errors, out List<DateTimeOffset> eventTimestamps)
    {
        eventTimestamps = new List<DateTimeOffset>();
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

                eventTimestamps.Add(owsEvent.TimestampUtc);

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

        var timelineEntry = archive.GetEntry(OwsConstants.TimelineFileName);
        if (timelineEntry is null)
        {
            errors.Add($"Missing timeline entry in package: {OwsConstants.TimelineFileName}");
        }
        else
        {
            using var timelineReader = new StreamReader(timelineEntry.Open());
            var timelineText = timelineReader.ReadToEnd();
            var actualTimelineHash = hashService.ComputeHash(timelineText);

            if (!string.Equals(actualTimelineHash, manifest.TimelineHash, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Timeline hash does not match manifest.");
            }
        }

        var graphEntry = archive.GetEntry(OwsConstants.VersionGraphFileName);
        if (graphEntry is null)
        {
            errors.Add($"Missing version graph entry in package: {OwsConstants.VersionGraphFileName}");
        }
        else
        {
            using var graphReader = new StreamReader(graphEntry.Open());
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
}
