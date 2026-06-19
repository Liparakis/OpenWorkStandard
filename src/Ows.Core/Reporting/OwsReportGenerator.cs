using System.Text.Json;
using System.Text;

namespace Ows.Core.Reporting;

/// <summary>
/// Provides the initial report generation skeleton.
/// </summary>
public sealed class OwsReportGenerator : IReportGenerator
{
    /// <inheritdoc />
    public Task<ReportGenerationResult> GenerateAsync(ReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var status = request.VerificationResult.IsSuccess ? "Success" : "Failure";
        var trust = request.VerificationResult.TrustStatus.ToString();
        var errors = request.VerificationResult.Errors.Count == 0
            ? ["None"]
            : request.VerificationResult.Errors.ToArray();

        var content = request.Format switch
        {
            ReportFormat.Text => BuildTextReport(status, trust, request, errors),
            ReportFormat.Json => JsonSerializer.Serialize(
                new
                {
                    status,
                    trust,
                    summary = request.VerificationResult.Summary,
                    errors,
                    findings = request.VerificationResult.Findings.Select(finding => new
                    {
                        finding.Code,
                        finding.Title,
                        finding.Detail
                    }),
                    reviewSignals = request.VerificationResult.ReviewSignals.Select(signal => new
                    {
                        signal.SignalType,
                        signal.Title,
                        signal.Detail,
                        signal.Severity
                    })
                },
                new JsonSerializerOptions { WriteIndented = true }),
            _ => throw new NotSupportedException($"Report format '{request.Format}' is not supported yet.")
        };

        return Task.FromResult(new ReportGenerationResult
        {
            Format = request.Format,
            Content = content
        });
    }

    /// <summary>
    /// Builds the human-readable text review report.
    /// </summary>
    /// <param name="status">The verification status label.</param>
    /// <param name="trust">The trust label.</param>
    /// <param name="request">The source report request.</param>
    /// <param name="errors">The normalized error list.</param>
    /// <returns>The formatted report content.</returns>
    private static string BuildTextReport(
        string status,
        string trust,
        ReportRequest request,
        IReadOnlyList<string> errors)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Open Work Standard Review Report");
        builder.AppendLine();
        builder.AppendLine($"Status: {status}");
        builder.AppendLine($"Trust: {trust}");
        builder.AppendLine($"Summary: {request.VerificationResult.Summary}");
        builder.AppendLine();
        builder.AppendLine("Findings:");

        if (request.VerificationResult.Findings.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (var finding in request.VerificationResult.Findings)
            {
                builder.AppendLine($"- {finding.Title} ({finding.Code})");
                builder.AppendLine($"  {finding.Detail}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Review Signals:");

        if (request.VerificationResult.ReviewSignals.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (var signal in request.VerificationResult.ReviewSignals)
            {
                builder.AppendLine($"- {signal.Title} [{signal.SignalType}] severity {signal.Severity}");
                builder.AppendLine($"  {signal.Detail}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Errors:");
        foreach (var error in errors)
        {
            builder.AppendLine($"- {error}");
        }

        return builder.ToString().TrimEnd();
    }
}
