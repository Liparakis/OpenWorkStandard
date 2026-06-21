using System.Text.Json;
using Ows.Core.Verification;

namespace Ows.Core.Reporting;

/// <summary>
/// Provides JSON rendering capabilities for verification reports.
/// </summary>
internal static class JsonReportRenderer
{
    /// <summary>
    /// Serializes a <see cref="VerificationResult"/> instance into a formatted JSON string.
    /// </summary>
    /// <param name="res">The verification result containing trust details, timeline statistics, lease status, and findings to serialize.</param>
    /// <returns>A formatted JSON string representing the verification report.</returns>
    public static string BuildJsonReport(VerificationResult res)
    {
        return JsonSerializer.Serialize(
            new
            {
                status = res.TrustStatus.ToString(),
                recommendation = res.Recommendation,
                summary = res.Summary,
                generatedAt = res.GeneratedAt,
                package = new
                {
                    packageId = res.Package.PackageId,
                    packageHash = res.Package.PackageHash,
                    sessionId = res.Package.SessionId
                },
                timeline = new
                {
                    integrity = res.Timeline.Integrity,
                    eventCount = res.Timeline.EventCount,
                    headEventHash = res.Timeline.HeadEventHash
                },
                receipts = new
                {
                    alignment = res.Receipts.Alignment,
                    receiptCount = res.Receipts.ReceiptCount,
                    headReceiptHash = res.Receipts.HeadReceiptHash
                },
                lease = new
                {
                    status = res.Lease.Status,
                    lastHeartbeatAt = res.Lease.LastHeartbeatAt,
                    leaseExpiresAt = res.Lease.LeaseExpiresAt,
                    gaps = res.Lease.Gaps.Select(g => new
                    {
                        startTime = g.StartTime.ToString("o"),
                        durationSeconds = g.DurationSeconds
                    })
                },
                anchor = new
                {
                    status = res.Anchor.Status,
                    anchoredAt = res.Anchor.AnchoredAt,
                    anchoredSessionHead = res.Anchor.AnchoredSessionHead
                },
                findings = res.Findings.Select(finding => new
                {
                    code = finding.Code,
                    severity = finding.Severity,
                    title = finding.Title,
                    detail = finding.Detail,
                    technicalDetail = finding.TechnicalDetail,
                    reviewerAction = finding.ReviewerAction
                }),
                education = res.Education != null
                    ? new
                    {
                        institutionId = res.Education.InstitutionId,
                        institutionName = res.Education.InstitutionName,
                        courseId = res.Education.CourseId,
                        courseCode = res.Education.CourseCode,
                        courseTitle = res.Education.CourseTitle,
                        classGroupId = res.Education.ClassGroupId,
                        classGroupName = res.Education.ClassGroupName,
                        assessmentId = res.Education.AssessmentId,
                        assessmentTitle = res.Education.AssessmentTitle,
                        studentUserId = res.Education.StudentUserId,
                        studentDisplayName = res.Education.StudentDisplayName,
                        studentExternalId = res.Education.StudentExternalId
                    }
                    : (object?)null
            },
            new JsonSerializerOptions { WriteIndented = true });
    }
}