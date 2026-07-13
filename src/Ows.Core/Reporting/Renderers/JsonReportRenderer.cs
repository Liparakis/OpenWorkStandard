using System.Text.Json;
using Ows.Core.Verification;

namespace Ows.Core.Reporting;

internal static class JsonReportRenderer {
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static string BuildJsonReport(VerificationResult res) => JsonSerializer.Serialize(new {
        status = res.TrustStatus.ToString(),
        signatureStatus = res.SignatureStatus,
        recommendation = res.Recommendation,
        summary = res.Summary,
        trustExplanation = res.TrustExplanation,
        generatedAt = res.GeneratedAt,
        package = new {
            packageId = res.Package.PackageId,
            packageHash = res.Package.PackageHash,
            packageRootHash = res.Package.PackageRootHash
        },
        timeline = new {
            integrity = res.Timeline.Integrity,
            eventCount = res.Timeline.EventCount,
            headEventHash = res.Timeline.HeadEventHash
        },
        findings = res.Findings.Select(finding => new {
            code = finding.Code,
            severity = finding.Severity,
            title = finding.Title,
            detail = finding.Detail,
            technicalDetail = finding.TechnicalDetail,
            reviewerAction = finding.ReviewerAction
        }),
        errors = res.Errors
    }, SerializerOptions);
}
