using System.Text.Json;

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
            ReportFormat.Text => $"Status: {status}{Environment.NewLine}Trust: {trust}{Environment.NewLine}Summary: {request.VerificationResult.Summary}{Environment.NewLine}Errors: {string.Join(", ", errors)}",
            ReportFormat.Json => JsonSerializer.Serialize(
                new
                {
                    status,
                    trust,
                    summary = request.VerificationResult.Summary,
                    errors
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
}
