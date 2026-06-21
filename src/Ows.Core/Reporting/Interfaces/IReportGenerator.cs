namespace Ows.Core.Reporting;

/// <summary>
/// Defines report generation for verification outcomes.
/// </summary>
public interface IReportGenerator {
    /// <summary>
    /// Generates a report for the provided request.
    /// </summary>
    /// <param name="request">The report request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A placeholder report result.</returns>
    Task<ReportGenerationResult> GenerateAsync(ReportRequest request, CancellationToken cancellationToken);
}
