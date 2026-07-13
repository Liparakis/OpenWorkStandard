using Ows.Core.Reporting.Renderers;

namespace Ows.Core.Reporting;

/// <summary>
/// Provides reviewer-facing report generation for verification outcomes by delegating to renderers.
/// </summary>
public sealed class OwsReportGenerator {
    /// <inheritdoc />
    public static Task<ReportGenerationResult> GenerateAsync(ReportRequest request, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var res = request.VerificationResult;

        var content = request.Format switch {
            ReportFormat.Json => JsonReportRenderer.BuildJsonReport(res),
            ReportFormat.Text => TextReportRenderer.BuildTextReport(res),
            _ => throw new NotSupportedException($"Report format '{request.Format}' is not supported yet.")
        };

        return Task.FromResult(
            new ReportGenerationResult {
                Format = request.Format,
                Content = content
            }
        );
    }
}
