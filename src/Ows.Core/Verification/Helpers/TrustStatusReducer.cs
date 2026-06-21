using System.IO.Compression;
using Ows.Core.Notarization;

namespace Ows.Core.Verification;

/// <summary>
/// Verification helper that validates local vs. trusted remote notarization receipts to determine the final trust status.
/// </summary>
internal static class TrustStatusReducer {
    /// <summary>
    /// Resolves and verifies packaged and remote receipt chains, comparing alignment, sequence numbers, and head hashes to return a final <see cref="TrustStatus"/>.
    /// </summary>
    /// <param name="archive">The ZIP package container.</param>
    /// <param name="timelineHeadHash">The local timeline head event hash.</param>
    /// <param name="trustedReceiptChain">Optional receipt chain obtained directly from the trusted remote verifier.</param>
    /// <param name="trustedSessionHead">Optional session head status details from the trusted remote verifier.</param>
    /// <param name="errors">The list to accumulate verification errors.</param>
    /// <param name="findings">The list to append findings to.</param>
    /// <param name="verifiedKeyFingerprints">Out parameter containing a list of unique verification key fingerprints encountered.</param>
    /// <returns>The calculated <see cref="TrustStatus"/> level representing validation outcome.</returns>
    public static TrustStatus ValidateReceipts(
        ZipArchive archive,
        string timelineHeadHash,
        ReceiptChain? trustedReceiptChain,
        SessionHeadResponse? trustedSessionHead,
        List<string> errors,
        List<VerificationFinding> findings,
        out List<string> verifiedKeyFingerprints) {
        verifiedKeyFingerprints = [];
        var packagedReceiptChain = PackageStructureVerifier.ReadPackagedReceiptChain(archive, errors);
        if (packagedReceiptChain is not null) {
            foreach (var receipt in packagedReceiptChain.Receipts) {
                if (!string.IsNullOrWhiteSpace(receipt.SigningKeyFingerprint) &&
                    !verifiedKeyFingerprints.Contains(receipt.SigningKeyFingerprint)) {
                    verifiedKeyFingerprints.Add(receipt.SigningKeyFingerprint);
                }
            }
        }

        if (trustedReceiptChain is not null) {
            foreach (var receipt in trustedReceiptChain.Receipts) {
                if (!string.IsNullOrWhiteSpace(receipt.SigningKeyFingerprint) &&
                    !verifiedKeyFingerprints.Contains(receipt.SigningKeyFingerprint)) {
                    verifiedKeyFingerprints.Add(receipt.SigningKeyFingerprint);
                }
            }
        }

        if (errors.Count > 0) {
            return TrustStatus.Invalid;
        }

        if (trustedReceiptChain is not null) {
            if (!ReceiptChainVerifier.IsValid(trustedReceiptChain)) {
                errors.Add("Trusted remote receipt chain is invalid.");
                return TrustStatus.Invalid;
            }

            if (packagedReceiptChain is null) {
                var trustedLastReceipt = trustedReceiptChain.Receipts.LastOrDefault();
                if (trustedLastReceipt is null) {
                    findings.Add(VerificationFindingFactory.ReceiptChainMissingFinding);
                    return TrustStatus.Unverified;
                }

                if (!string.Equals(trustedLastReceipt.TimelineHeadHash, timelineHeadHash,
                        StringComparison.OrdinalIgnoreCase)) {
                    errors.Add("Trusted remote receipt chain head does not match the local timeline head.");
                    return TrustStatus.Invalid;
                }

                findings.Add(VerificationFindingFactory.ReceiptChainMissingFinding);
                return TrustStatus.Unverified;
            }

            if (!AreEquivalentReceiptChains(packagedReceiptChain, trustedReceiptChain)) {
                errors.Add("Packaged receipt chain does not match the trusted remote receipt chain.");
                return TrustStatus.Invalid;
            }
        } else if (trustedSessionHead is not null) {
            if (string.IsNullOrWhiteSpace(trustedSessionHead.SessionId)) {
                errors.Add("Trusted remote session head is invalid.");
                return TrustStatus.Invalid;
            }

            if (packagedReceiptChain is null) {
                if (!string.Equals(trustedSessionHead.LastTimelineHeadHash, timelineHeadHash,
                        StringComparison.OrdinalIgnoreCase)) {
                    errors.Add("Trusted remote session head does not match the local timeline head.");
                    return TrustStatus.Invalid;
                }

                findings.Add(VerificationFindingFactory.ReceiptChainMissingFinding);
                return TrustStatus.Unverified;
            }

            var packagedLastReceipt = packagedReceiptChain.Receipts.LastOrDefault();
            if (packagedLastReceipt is null) {
                findings.Add(VerificationFindingFactory.ReceiptChainMissingFinding);
                return TrustStatus.Unverified;
            }

            if (packagedLastReceipt.SequenceNumber != trustedSessionHead.LastSequenceNumber ||
                !string.Equals(packagedLastReceipt.TimelineHeadHash, trustedSessionHead.LastTimelineHeadHash,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(packagedLastReceipt.ReceiptHash, trustedSessionHead.LastReceiptHash,
                    StringComparison.OrdinalIgnoreCase)) {
                errors.Add("Packaged receipt head does not match the trusted remote session head.");
                return TrustStatus.Invalid;
            }
        }

        if (packagedReceiptChain is null) {
            findings.Add(VerificationFindingFactory.ReceiptChainMissingFinding);
            return TrustStatus.Unverified;
        }

        if (!ReceiptChainVerifier.IsValid(packagedReceiptChain)) {
            errors.Add("Receipt chain is invalid.");
            return TrustStatus.Invalid;
        }

        var lastReceipt = packagedReceiptChain.Receipts.LastOrDefault();
        if (lastReceipt is null) {
            findings.Add(VerificationFindingFactory.ReceiptChainMissingFinding);
            return TrustStatus.Unverified;
        }

        if (!string.Equals(lastReceipt.TimelineHeadHash, timelineHeadHash, StringComparison.OrdinalIgnoreCase)) {
            errors.Add("Receipt chain head does not match the local timeline head.");
            return TrustStatus.Invalid;
        }

        findings.Add(VerificationFindingFactory.ReceiptChainValidFinding);
        return TrustStatus.Verified;
    }

    /// <summary>
    /// Compares two receipt chains to determine if they contain equivalent sessions and matching receipts.
    /// </summary>
    /// <param name="left">The first receipt chain.</param>
    /// <param name="right">The second receipt chain.</param>
    /// <returns><see langword="true"/> if both receipt chains match; otherwise, <see langword="false"/>.</returns>
    public static bool AreEquivalentReceiptChains(ReceiptChain left, ReceiptChain right) =>
        left.SessionId == right.SessionId &&
        left.Receipts.SequenceEqual(right.Receipts);
}
