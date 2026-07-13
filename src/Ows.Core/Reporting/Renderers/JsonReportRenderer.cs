using System.Text.Json;
using Ows.Core.Verification;

namespace Ows.Core.Reporting.Renderers;

/// <summary>
///     Represents the <see cref="JsonReportRenderer" /> type.
/// </summary>
internal static class JsonReportRenderer {
    /// <summary>
    ///     Serialization options configured for indented JSON report rendering.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>
    ///     Serializes a verification result into a structured JSON string.
    /// </summary>
    /// <returns>A JSON string containing the formatted verification report.</returns>
    /// <param name="res">The verification result to serialize.</param>
    public static string BuildJsonReport(VerificationResult res) {
        return JsonSerializer.Serialize(
            new {
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
                activity = new {
                    meaningfulEventCount = res.Activity.MeaningfulEventCount,
                    distinctFileCount = res.Activity.DistinctFileCount,
                    createdFileCount = res.Activity.CreatedFileCount,
                    revisedFileCount = res.Activity.RevisedFileCount,
                    firstActivityUtc = res.Activity.FirstActivityUtc,
                    lastActivityUtc = res.Activity.LastActivityUtc,
                    activitySpanSeconds = res.Activity.ActivitySpanSeconds,
                    largestBurstEventCount = res.Activity.LargestBurstEventCount,
                    largestBurstSpanSeconds = res.Activity.LargestBurstSpanSeconds,
                    largestCreationWindowFileCount = res.Activity.LargestCreationWindowFileCount
                },
                findings = res.Findings.Select(finding => new {
                    code = finding.Code,
                    severity = finding.Severity,
                    title = finding.Title,
                    detail = finding.Detail,
                    technicalDetail = finding.TechnicalDetail,
                    reviewerAction = finding.ReviewerAction
                }
                ),
                errors = res.Errors
            }, SerializerOptions
        );
    }
}
