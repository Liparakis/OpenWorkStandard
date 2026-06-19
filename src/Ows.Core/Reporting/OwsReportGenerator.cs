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
            ? "None"
            : string.Join(", ", request.VerificationResult.Errors);

        var content = request.Format switch
        {
            ReportFormat.Text => $"Status: {status}{Environment.NewLine}Trust: {trust}{Environment.NewLine}Summary: {request.VerificationResult.Summary}{Environment.NewLine}Errors: {errors}",
            _ => $"Status: {status}{Environment.NewLine}Trust: {trust}{Environment.NewLine}Summary: {request.VerificationResult.Summary}{Environment.NewLine}Errors: {errors}"
        };

        return Task.FromResult(new ReportGenerationResult
        {
            Format = request.Format,
            Content = content
        });
    }
}
