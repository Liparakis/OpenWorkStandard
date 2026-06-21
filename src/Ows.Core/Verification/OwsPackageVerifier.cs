using System.IO.Compression;
using Ows.Core.Hashing;
using Ows.Core.Packaging;

namespace Ows.Core.Verification;

/// <summary>
/// Provides the package verification delegating to focused helper services.
/// </summary>
public sealed class OwsPackageVerifier : IPackageVerifier
{
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

        var packageHash = string.Empty;
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

        // 1. Structure Verification
        PackageStructureVerifier.VerifyRequiredEntries(archive, errors);

        OwsManifest? manifest = null;
        string? timelineHeadHash = null;
        var eventTimestamps = new List<DateTimeOffset>();

        bool sawObservationGap = false;
        bool sawLargeUnobservedChange = false;
        bool sawUnobservedChange = false;

        if (archive.GetEntry(OwsConstants.ManifestFileName) is not null)
        {
            manifest = PackageStructureVerifier.ValidateManifest(archive, errors);
        }

        if (archive.GetEntry(OwsConstants.TimelineFileName) is not null)
        {
            timelineHeadHash = TimelineIntegrityVerifier.ValidateTimeline(archive, errors, out eventTimestamps);
            ObservationContinuityAnalyzer.AnalyzeTimelineContinuity(
                archive, findings, out sawObservationGap, out sawLargeUnobservedChange, out sawUnobservedChange);
        }

        if (archive.GetEntry(OwsConstants.VersionGraphFileName) is not null)
        {
            PackageStructureVerifier.ValidateVersionGraph(archive, errors);
        }

        // Timeline Integrity Finding
        if (string.IsNullOrEmpty(timelineHeadHash) ||
            errors.Any(e => e.Contains("timeline", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(VerificationFindingFactory.TimelineChainBrokenFinding);
        }
        else
        {
            findings.Add(VerificationFindingFactory.TimelineChainValidFinding);
        }

        // Hash Validation Finding
        if (manifest is not null)
        {
            var initialErrorCount = errors.Count;
            ArtifactHashVerifier.ValidateHashes(archive, manifest, errors);
            if (errors.Count > initialErrorCount)
            {
                findings.Add(VerificationFindingFactory.PackageHashInvalidFinding);
            }
        }

        var trustStatus = TrustStatus.Invalid;
        var verifiedKeyFingerprints = new List<string>();

        if (errors.Count == 0 && manifest is not null)
        {
            trustStatus = TrustStatusReducer.ValidateReceipts(
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
                    findings.Add(VerificationFindingFactory.LeaseGapLongFinding);
                    leaseStatus = "Unverified";
                    if (trustStatus == TrustStatus.Verified || trustStatus == TrustStatus.Degraded)
                    {
                        trustStatus = TrustStatus.Unverified;
                    }
                }
                else
                {
                    findings.Add(VerificationFindingFactory.LeaseGapShortFinding);
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
                    findings.Add(VerificationFindingFactory.LeaseWorkAfterExpirationFinding);
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
                var expectedHead = request.TrustedSessionHead?.LastTimelineHeadHash ??
                                   request.TrustedReceiptChain?.Receipts.LastOrDefault()?.TimelineHeadHash;
                anchoredSessionHead = expectedHead ?? "None";

                var matches = string.Equals(expectedHead, timelineHeadHash, StringComparison.OrdinalIgnoreCase);
                if (matches)
                {
                    findings.Add(VerificationFindingFactory.PackageAnchorValidFinding);
                    anchorStatus = "Anchored";
                }
                else
                {
                    findings.Add(VerificationFindingFactory.VerifierSessionHeadMismatchFinding);
                    findings.Add(VerificationFindingFactory.PackageAnchorMissingFinding);
                    anchorStatus = "Mismatch";
                }
            }
            else
            {
                findings.Add(VerificationFindingFactory.PackageAnchorMissingFinding);
                anchorStatus = "Missing";
            }
        }

        if (errors.Count == 0 && trustStatus != TrustStatus.Invalid)
        {
            if (sawLargeUnobservedChange)
            {
                if (trustStatus == TrustStatus.Verified)
                {
                    trustStatus = TrustStatus.Degraded;
                }
            }
            else if (sawUnobservedChange || sawObservationGap)
            {
                if (trustStatus == TrustStatus.Verified)
                {
                    trustStatus = TrustStatus.Degraded;
                }
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
            TrustStatus.Degraded =>
                "The submission has minor or bounded continuity issues. The evidence is mostly usable, but manual review is recommended.",
            TrustStatus.Unverified =>
                "The package is structurally valid, but OWS cannot verify enough of the work timeline or remote session continuity.",
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
        var packagedReceiptChain = PackageStructureVerifier.ReadPackagedReceiptChain(archive, new List<string>());
        var sessionId = PackageStructureVerifier.ReadSessionId(archive)
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
            Integrity = (string.IsNullOrEmpty(timelineHeadHash) ||
                         errors.Any(e => e.Contains("timeline", StringComparison.OrdinalIgnoreCase)))
                ? "Broken"
                : "Valid",
            EventCount = eventTimestamps.Count,
            HeadEventHash = timelineHeadHash ?? "None"
        };

        var receiptsAlignment = "Missing";
        if (request.TrustedReceiptChain is not null || packagedReceiptChain is not null)
        {
            var matched = true;
            if (request.TrustedReceiptChain is not null && packagedReceiptChain is not null)
            {
                matched = TrustStatusReducer.AreEquivalentReceiptChains(packagedReceiptChain,
                    request.TrustedReceiptChain);
            }

            receiptsAlignment = matched ? "Aligned" : "Misaligned";
        }

        var receiptsInfo = new ReportReceiptsInfo
        {
            Alignment = receiptsAlignment,
            ReceiptCount = packagedReceiptChain?.Receipts.Count ?? request.TrustedReceiptChain?.Receipts.Count ?? 0,
            HeadReceiptHash = packagedReceiptChain?.Receipts.LastOrDefault()?.ReceiptHash ??
                              request.TrustedReceiptChain?.Receipts.LastOrDefault()?.ReceiptHash ?? "None"
        };

        var leaseInfo = new ReportLeaseInfo
        {
            Status = leaseStatus,
            LastHeartbeatAt = request.SessionLastHeartbeatAt?.ToString("o") ?? "None",
            LeaseExpiresAt = request.SessionLeaseExpiresAt?.ToString("o") ?? "None",
            Gaps = gaps
        };

        var anchoredAt =
            request.TrustedReceiptChain?.Receipts.LastOrDefault()?.ServerTimestamp.IssuedAtUtc.ToString("o") ?? "None";
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
}