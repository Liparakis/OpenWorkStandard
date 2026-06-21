namespace Ows.Core.Reporting;

/// <summary>
/// Provides professor-facing report generation for verification outcomes by delegating to renderers.
/// </summary>
public sealed class OwsReportGenerator : IReportGenerator {
    /// <inheritdoc />
    public Task<ReportGenerationResult> GenerateAsync(ReportRequest request, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var res = request.VerificationResult;

        var content = request.Format switch {
            ReportFormat.Json => JsonReportRenderer.BuildJsonReport(res),
            ReportFormat.Text => TextReportRenderer.BuildTextReport(res),
            _ => throw new NotSupportedException($"Report format '{request.Format}' is not supported yet.")
        };

        return Task.FromResult(new ReportGenerationResult {
            Format = request.Format,
            Content = content
        });
    }
}
