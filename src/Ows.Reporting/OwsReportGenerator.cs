namespace Ows.Reporting;

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

        return Task.FromResult(new ReportGenerationResult
        {
            Format = request.Format,
            Content = "OWS report: not implemented yet"
        });
    }
}
