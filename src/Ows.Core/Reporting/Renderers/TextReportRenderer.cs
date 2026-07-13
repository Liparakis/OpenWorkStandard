using System.Text;
using Ows.Core.Verification;

namespace Ows.Core.Reporting;

/// <summary>
/// Provides text rendering capabilities for verification reports.
/// </summary>
internal static class TextReportRenderer {
    /// <summary>
    /// Formats a <see cref="VerificationResult"/> instance into a human-readable text report.
    /// </summary>
    /// <param name="res">The verification result containing trust details, timeline statistics, lease status, and findings to render.</param>
    /// <returns>A formatted human-readable text report string.</returns>
    public static string BuildTextReport(VerificationResult res) {
        var builder = new StringBuilder();
        builder.AppendLine("OWS Verification Report");
        builder.AppendLine(
            "Event presence is evidence of recorded activity. Event absence is not proof of misconduct.");
        builder.AppendLine($"Status: {res.TrustStatus}");
        builder.AppendLine($"Package Signature: {res.SignatureStatus}");
        builder.AppendLine($"Recommendation: {res.Recommendation}");
        builder.AppendLine();

        builder.AppendLine("Summary:");
        builder.AppendLine(res.Summary);
        builder.AppendLine($"Trust Grade Explanation: {res.TrustExplanation}");
        builder.AppendLine();

        builder.AppendLine("External Context Metadata:");
        if (res.ExternalContext == null ||
            (string.IsNullOrEmpty(res.ExternalContext.InstitutionId) &&
             string.IsNullOrEmpty(res.ExternalContext.AssessmentId) &&
             string.IsNullOrEmpty(res.ExternalContext.StudentUserId))) {
            builder.AppendLine("External context metadata was not provided.");
        } else {
            AppendContextValue(builder, "Institution ID", res.ExternalContext.InstitutionId);
            AppendContextValue(builder, "Assessment ID", res.ExternalContext.AssessmentId);
            AppendContextValue(builder, "Student ID", res.ExternalContext.StudentUserId);
        }

        builder.AppendLine();

        builder.AppendLine("Verification Scope:");
        builder.AppendLine($"- Package ID: {res.Package.PackageId}");
        builder.AppendLine($"- Package Hash: {res.Package.PackageHash}");
        builder.AppendLine($"- Package Root Hash: {res.Package.PackageRootHash}");
        builder.AppendLine($"- Session ID: {res.Package.SessionId}");
        builder.AppendLine();

        builder.AppendLine("Timeline Integrity:");
        builder.AppendLine($"- Integrity: {res.Timeline.Integrity}");
        builder.AppendLine($"- Total Events: {res.Timeline.EventCount}");
        builder.AppendLine($"- Head Event Hash: {res.Timeline.HeadEventHash}");
        builder.AppendLine();

        builder.AppendLine("Remote Receipt Alignment:");
        builder.AppendLine($"- Alignment: {res.Receipts.Alignment}");
        builder.AppendLine($"- Receipt Count: {res.Receipts.ReceiptCount}");
        builder.AppendLine($"- Head Receipt Hash: {res.Receipts.HeadReceiptHash}");
        builder.AppendLine();

        builder.AppendLine("Session Lease Continuity:");
        builder.AppendLine($"- Lease Status: {res.Lease.Status}");
        builder.AppendLine($"- Last Heartbeat: {res.Lease.LastHeartbeatAt}");
        builder.AppendLine($"- Lease Expiry: {res.Lease.LeaseExpiresAt}");

        var gapsCount = res.Lease.Gaps.Count;
        builder.AppendLine($"- Gaps: {gapsCount}");

        var maxGapSeconds = res.Lease.Gaps.Count > 0 ? res.Lease.Gaps.Max(g => g.DurationSeconds) : 0;
        builder.AppendLine($"- Longest gap: {FormatDuration(maxGapSeconds)}");

        builder.AppendLine(gapsCount > 0
            ? "- Impact: Work during this interval cannot be automatically verified. Evidence gaps exist."
            : "- Impact: None. Session continuity has no verified gaps.");
        builder.AppendLine();

        builder.AppendLine("Package Anchor Status:");
        builder.AppendLine($"- Anchor Status: {res.Anchor.Status}");
        builder.AppendLine($"- Anchored At: {res.Anchor.AnchoredAt}");
        builder.AppendLine($"- Anchored Session Head: {res.Anchor.AnchoredSessionHead}");
        builder.AppendLine();

        builder.AppendLine("Findings:");
        if (res.Findings.Count == 0) {
            builder.AppendLine("- None");
        } else {
            foreach (var finding in res.Findings) {
                builder.AppendLine($"[{finding.Severity}] {finding.Code}");
                builder.AppendLine($"{finding.Title}: {finding.Detail}");
                builder.AppendLine($"Suggested Action: {finding.ReviewerAction}");
                builder.AppendLine($"Technical Details: {finding.TechnicalDetail}");
                builder.AppendLine();
            }
        }

        builder.AppendLine("Affected Files:");
        builder.AppendLine(
            "Exact file impact is unavailable. File events are recorded in the timeline but OWS cannot automatically map lease gaps to specific local files without manual inspection.");
        builder.AppendLine();

        builder.AppendLine("Manual Review Suggestions:");
        var suggestions = GetReviewSuggestions(res.TrustStatus);
        foreach (var suggestion in suggestions) {
            builder.AppendLine(suggestion);
        }

        builder.AppendLine();

        builder.AppendLine("Technical Details:");
        builder.AppendLine($"- Generated At: {res.GeneratedAt}");
        if (res.VerifiedKeyFingerprints.Count > 0) {
            builder.AppendLine($"- Verifier Key Fingerprints: {string.Join(", ", res.VerifiedKeyFingerprints)}");
        }

        if (res.Errors.Count > 0) {
            builder.AppendLine("- Errors:");
            foreach (var error in res.Errors) {
                builder.AppendLine($"  - {error}");
            }
        } else {
            builder.AppendLine("- Errors: None");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Formats a duration in seconds into a human-readable string (e.g. "1d 2h 30m 15s").
    /// </summary>
    /// <param name="seconds">The duration in seconds.</param>
    /// <returns>A formatted duration string.</returns>
    private static string FormatDuration(double seconds) {
        var t = TimeSpan.FromSeconds(seconds);
        var parts = new List<string>();
        if (t.Days > 0) parts.Add($"{t.Days}d");
        if (t.Hours > 0) parts.Add($"{t.Hours}h");
        if (t.Minutes > 0) parts.Add($"{t.Minutes}m");
        if (t.Seconds > 0 || parts.Count == 0) parts.Add($"{t.Seconds}s");
        return string.Join(" ", parts);
    }

    private static void AppendContextValue(StringBuilder builder, string label, string? value) {
        if (!string.IsNullOrWhiteSpace(value)) {
            builder.AppendLine($"- {label}: {value}");
        }
    }

    /// <summary>
    /// Returns suggested actions for a manual reviewer based on the trust status of the verification.
    /// </summary>
    /// <param name="status">The trust status level.</param>
    /// <returns>A list of suggested verification or inspection steps.</returns>
    private static IReadOnlyList<string> GetReviewSuggestions(TrustStatus status) {
        return status switch {
            TrustStatus.Verified =>
            [
                "- None. The submission is automatically verified."
            ],
            TrustStatus.Degraded =>
            [
                "- Review the timeline event sequence around the reported lease gaps.",
                "- Verify whether the gaps correspond to expected student breaks or pauses.",
                "- Review the diffs of changes committed immediately after the lease gap."
            ],
            TrustStatus.Unverified =>
            [
                "- Conduct a thorough manual review of the codebase.",
                "- Cross-reference the git history with the OWS timeline sequence.",
                "- Inspect the version graph for timeline discontinuities or bulk imports."
            ],
            _ =>
            [
                "- Request the student to resubmit the package.",
                "- Investigate why the package hashes, receipt signatures, or timeline event chain are broken."
            ]
        };
    }
}
