using System.IO.Compression;
using Ows.Core.Hashing;
using Ows.Core.Packaging;

namespace Ows.Core.Verification;

/// <summary>
/// Verifies OWS packages using only the package contents and local verification rules.
/// </summary>
public sealed class OwsPackageVerifier {
    /// <inheritdoc />
    public Task<VerificationResult> VerifyAsync(PackageVerificationRequest request, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var generatedAt = DateTimeOffset.UtcNow.ToString("o");
        var errors = new List<string>();
        var findings = new List<VerificationFinding>();
        var packageHash = string.Empty;

        if (!File.Exists(request.PackagePath)) {
            return Task.FromResult(Failure(
                $"Package file not found: {request.PackagePath}",
                generatedAt));
        }

        try {
            packageHash = new Sha256HashService().ComputeHash(File.ReadAllBytes(request.PackagePath));
        } catch (Exception ex) {
            errors.Add($"Failed to compute package hash: {ex.Message}");
        }

        try {
            using var archive = ZipFile.OpenRead(request.PackagePath);
            PackageStructureVerifier.VerifyRequiredEntries(archive, errors);

            OwsManifest? manifest = null;
            var signatureStatus = "Unsigned";
            if (archive.GetEntry(OwsConstants.ManifestFileName) is not null) {
                manifest = PackageStructureVerifier.ValidateManifest(archive, errors);
                if (manifest is not null) {
                    signatureStatus = PackageSignatureVerifier.Validate(archive, manifest, errors);
                }
            }

            string? timelineHeadHash = null;
            var eventTimestamps = new List<DateTimeOffset>();
            var sawObservationGap = false;
            var sawLargeUnobservedChange = false;
            var sawUnobservedChange = false;

            if (archive.GetEntry(OwsConstants.TimelineFileName) is not null) {
                timelineHeadHash = TimelineIntegrityVerifier.ValidateTimeline(
                    archive, errors, out eventTimestamps);
                ObservationContinuityAnalyzer.AnalyzeTimelineContinuity(
                    archive, findings, out sawObservationGap, out sawLargeUnobservedChange, out sawUnobservedChange);
            }

            if (archive.GetEntry(OwsConstants.VersionGraphFileName) is not null) {
                PackageStructureVerifier.ValidateVersionGraph(archive, errors);
            }

            var timelineValid = !string.IsNullOrEmpty(timelineHeadHash) &&
                                !errors.Any(error => error.Contains("timeline", StringComparison.OrdinalIgnoreCase));
            findings.Add(timelineValid
                ? VerificationFindingFactory.TimelineChainValidFinding
                : VerificationFindingFactory.TimelineChainBrokenFinding);

            if (manifest is not null) {
                var errorCount = errors.Count;
                ArtifactHashVerifier.ValidateHashes(archive, manifest, errors);
                if (errors.Count > errorCount) {
                    findings.Add(VerificationFindingFactory.PackageHashInvalidFinding);
                }
            }

            var trustStatus = errors.Count > 0
                ? TrustStatus.Invalid
                : signatureStatus == "Valid" ? TrustStatus.Verified : TrustStatus.Unverified;

            if (trustStatus != TrustStatus.Invalid &&
                (sawObservationGap || sawLargeUnobservedChange || sawUnobservedChange) &&
                trustStatus == TrustStatus.Verified) {
                trustStatus = TrustStatus.Degraded;
            }

            var packageInfo = new ReportPackageInfo {
                PackageId = manifest?.PackageId ?? "Unknown",
                PackageHash = packageHash,
                PackageRootHash = manifest?.PackageRootHash ?? string.Empty
            };
            var timelineInfo = new ReportTimelineInfo {
                Integrity = timelineValid ? "Valid" : "Broken",
                EventCount = eventTimestamps.Count,
                HeadEventHash = timelineHeadHash ?? "None"
            };
            var isSuccess = trustStatus != TrustStatus.Invalid;

            return Task.FromResult(new VerificationResult {
                IsSuccess = isSuccess,
                TrustStatus = trustStatus,
                Summary = isSuccess ? "OWS verify succeeded." : "OWS verify failed.",
                Errors = errors,
                Findings = findings,
                SignatureStatus = signatureStatus,
                TrustExplanation = GetTrustExplanation(trustStatus),
                Recommendation = GetRecommendation(trustStatus),
                GeneratedAt = generatedAt,
                Package = packageInfo,
                Timeline = timelineInfo
            });
        } catch (Exception ex) {
            errors.Add($"Package is not a readable ZIP archive: {ex.Message}");
            return Task.FromResult(Failure(
                "The package container cannot be read as a valid OWS archive.",
                generatedAt,
                errors,
                packageHash));
        }
    }

    private static VerificationResult Failure(
        string summary,
        string generatedAt,
        IReadOnlyList<string>? errors = null,
        string packageHash = "") => new() {
        IsSuccess = false,
        TrustStatus = TrustStatus.Invalid,
        Summary = "OWS verify failed.",
        Errors = errors ?? [summary],
        Findings = [],
        TrustExplanation = "The package container or local evidence is invalid.",
        Recommendation = "Reject as invalid package / request resubmission",
        GeneratedAt = generatedAt,
        Package = new ReportPackageInfo { PackageHash = packageHash }
    };

    private static string GetTrustExplanation(TrustStatus status) => status switch {
        TrustStatus.Verified => "The package signature, local timeline, version graph, and artifact hashes align.",
        TrustStatus.Degraded => "The package is locally valid, but its timeline records an observation continuity issue. Manual review is recommended.",
        TrustStatus.Unverified => "The package is locally valid, but it is unsigned. OWS cannot establish package authenticity from the package alone.",
        _ => "The package container, timeline, signature, or hashes are broken or inconsistent."
    };

    private static string GetRecommendation(TrustStatus status) => status switch {
        TrustStatus.Verified => "Accept as verified",
        TrustStatus.Degraded => "Manual review recommended",
        TrustStatus.Unverified => "Manual review required",
        _ => "Reject as invalid package / request resubmission"
    };
}
