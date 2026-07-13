using System.Text;
using Ows.Core.Verification;

namespace Ows.Core.Reporting.Renderers;

/// <summary>
/// Represents the <see cref="TextReportRenderer"/> type.
/// </summary>
internal static class TextReportRenderer {
    /// <summary>
    /// Builds a human-readable text verification report for a verification result.
    /// </summary>
    /// <returns>A formatted text report string.</returns>
    /// <param name="res">The verification result to build the report for.</param>
    public static string BuildTextReport(VerificationResult res) {
        var builder = new StringBuilder();
        builder.AppendLine("OWS Verification Report");
        builder.AppendLine(
            "Event presence is evidence of recorded activity. Event absence is not proof of misconduct."
        );
        builder.AppendLine($"Status: {res.TrustStatus}");
        builder.AppendLine($"Package Signature: {res.SignatureStatus}");
        builder.AppendLine($"Recommendation: {res.Recommendation}");
        builder.AppendLine();
        builder.AppendLine("Summary:");
        builder.AppendLine(res.Summary);
        builder.AppendLine($"Trust Grade Explanation: {res.TrustExplanation}");
        builder.AppendLine();
        builder.AppendLine("Verification Scope:");
        builder.AppendLine($"- Package ID: {res.Package.PackageId}");
        builder.AppendLine($"- Package Hash: {res.Package.PackageHash}");
        builder.AppendLine($"- Package Root Hash: {res.Package.PackageRootHash}");
        builder.AppendLine();
        builder.AppendLine("Timeline Integrity:");
        builder.AppendLine($"- Integrity: {res.Timeline.Integrity}");
        builder.AppendLine($"- Total Events: {res.Timeline.EventCount}");
        builder.AppendLine($"- Head Event Hash: {res.Timeline.HeadEventHash}");
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

        builder.AppendLine("Manual Review Suggestions:");
        foreach (var suggestion in GetReviewSuggestions(res.TrustStatus)) {
            builder.AppendLine(suggestion);
        }

        builder.AppendLine();
        builder.AppendLine("Technical Details:");
        builder.AppendLine($"- Generated At: {res.GeneratedAt}");
        if (res.Errors.Count == 0) {
            builder.AppendLine("- Errors: None");
        } else {
            builder.AppendLine("- Errors:");
            foreach (var error in res.Errors) {
                builder.AppendLine($"  - {error}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Returns a list of suggested reviewer actions based on the package's trust status.
    /// </summary>
    /// <returns>A read-only list of string suggestions.</returns>
    /// <param name="status">The trust status of the verification result.</param>
    private static IReadOnlyList<string> GetReviewSuggestions(TrustStatus status) => status switch {
        TrustStatus.Verified => ["- None. The package is locally verified."],
        TrustStatus.Degraded => ["- Review the timeline around the reported observation gap."],
        TrustStatus.Unverified => ["- Confirm the package source and review the timeline and version graph manually."],
        _ => ["- Request a resubmission and investigate the reported integrity errors."]
    };
}
