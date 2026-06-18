using FluentAssertions;
using Ows.Core.Reporting;
using Ows.Core.Verification;

namespace Ows.Core.Tests;

/// <summary>
/// Tests reporting types after consolidation into Ows.Core.
/// </summary>
public sealed class ReportingNamespaceTests
{
    /// <summary>
    /// Verifies that the report generator emits a useful text summary.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_ShouldReturnTextSummary()
    {
        var generator = new OwsReportGenerator();

        var result = await generator.GenerateAsync(
            new ReportRequest
            {
                Format = ReportFormat.Text,
                VerificationResult = VerificationResult.Success("OWS verify succeeded.")
            },
            CancellationToken.None);

        result.Format.Should().Be(ReportFormat.Text);
        result.Content.Should().Contain("Status: Success");
        result.Content.Should().Contain("OWS verify succeeded.");
    }
}
