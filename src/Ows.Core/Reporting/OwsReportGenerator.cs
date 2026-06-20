using System.Text.Json;
using System.Text;
using Ows.Core.Verification;

namespace Ows.Core.Reporting;

/// <summary>
/// Provides professor-facing report generation for verification outcomes.
/// </summary>
public sealed class OwsReportGenerator : IReportGenerator
{
    /// <inheritdoc />
    public Task<ReportGenerationResult> GenerateAsync(ReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var res = request.VerificationResult;
        
        var content = request.Format switch
        {
            ReportFormat.Json => BuildJsonReport(res),
            ReportFormat.Text => BuildTextReport(res),
            _ => throw new NotSupportedException($"Report format '{request.Format}' is not supported yet.")
        };

        return Task.FromResult(new ReportGenerationResult
        {
            Format = request.Format,
            Content = content
        });
    }

    private static string BuildJsonReport(VerificationResult res)
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
                education = res.Education != null ? new
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
                } : (object?)null
            },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildTextReport(VerificationResult res)
    {
        var builder = new StringBuilder();
        builder.AppendLine("OWS Verification Report");
        builder.AppendLine("Event presence is evidence of recorded activity. Event absence is not proof of misconduct.");
        builder.AppendLine($"Status: {res.TrustStatus}");
        builder.AppendLine($"Recommendation: {res.Recommendation}");
        builder.AppendLine();

        builder.AppendLine("Summary:");
        builder.AppendLine(res.Summary);
        builder.AppendLine($"Trust Grade Explanation: {res.TrustExplanation}");
        builder.AppendLine();

        builder.AppendLine("Assessment Context:");
        if (res.Education == null ||
            (string.IsNullOrEmpty(res.Education.InstitutionId) &&
             string.IsNullOrEmpty(res.Education.CourseId) &&
             string.IsNullOrEmpty(res.Education.ClassGroupId) &&
             string.IsNullOrEmpty(res.Education.AssessmentId) &&
             string.IsNullOrEmpty(res.Education.StudentUserId)))
        {
            builder.AppendLine("Assessment context was not provided.");
        }
        else
        {
            builder.AppendLine($"- Institution: {res.Education.InstitutionName ?? "Unknown"} (ID: {res.Education.InstitutionId ?? "Unknown"})");
            builder.AppendLine($"- Course: {res.Education.CourseCode ?? "Unknown"} - {res.Education.CourseTitle ?? "Unknown"} (ID: {res.Education.CourseId ?? "Unknown"})");
            builder.AppendLine($"- Class/Group: {res.Education.ClassGroupName ?? "Unknown"} (ID: {res.Education.ClassGroupId ?? "Unknown"})");
            builder.AppendLine($"- Assessment: {res.Education.AssessmentTitle ?? "Unknown"} (ID: {res.Education.AssessmentId ?? "Unknown"})");
            builder.AppendLine($"- Student: {res.Education.StudentDisplayName ?? "Unknown"} (External ID: {res.Education.StudentExternalId ?? "Unknown"}) (ID: {res.Education.StudentUserId ?? "Unknown"})");
            builder.AppendLine($"- Session ID: {res.Package.SessionId}");
            builder.AppendLine($"- Package ID: {res.Package.PackageId}");
        }
        builder.AppendLine();

        builder.AppendLine("Verification Scope:");
        builder.AppendLine($"- Package ID: {res.Package.PackageId}");
        builder.AppendLine($"- Package Hash: {res.Package.PackageHash}");
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
        if (res.Findings.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (var finding in res.Findings)
            {
                builder.AppendLine($"[{finding.Severity}] {finding.Code}");
                builder.AppendLine($"{finding.Title}: {finding.Detail}");
                builder.AppendLine($"Suggested Action: {finding.ReviewerAction}");
                builder.AppendLine($"Technical Details: {finding.TechnicalDetail}");
                builder.AppendLine();
            }
        }

        builder.AppendLine("Affected Files:");
        builder.AppendLine("Exact file impact is unavailable. File events are recorded in the timeline but OWS cannot automatically map lease gaps to specific local files without manual inspection.");
        builder.AppendLine();

        builder.AppendLine("Manual Review Suggestions:");
        var suggestions = GetReviewSuggestions(res.TrustStatus);
        foreach (var suggestion in suggestions)
        {
            builder.AppendLine(suggestion);
        }
        builder.AppendLine();

        builder.AppendLine("Technical Details:");
        builder.AppendLine($"- Generated At: {res.GeneratedAt}");
        if (res.VerifiedKeyFingerprints.Count > 0)
        {
            builder.AppendLine($"- Verifier Key Fingerprints: {string.Join(", ", res.VerifiedKeyFingerprints)}");
        }
        
        if (res.Errors.Count > 0)
        {
            builder.AppendLine("- Errors:");
            foreach (var error in res.Errors)
            {
                builder.AppendLine($"  - {error}");
            }
        }
        else
        {
            builder.AppendLine("- Errors: None");
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatDuration(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        var parts = new List<string>();
        if (t.Days > 0) parts.Add($"{t.Days}d");
        if (t.Hours > 0) parts.Add($"{t.Hours}h");
        if (t.Minutes > 0) parts.Add($"{t.Minutes}m");
        if (t.Seconds > 0 || parts.Count == 0) parts.Add($"{t.Seconds}s");
        return string.Join(" ", parts);
    }

    private static IReadOnlyList<string> GetReviewSuggestions(TrustStatus status)
    {
        return status switch
        {
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