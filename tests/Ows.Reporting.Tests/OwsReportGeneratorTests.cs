using FluentAssertions;
using Ows.Core.Verification;
using Ows.Reporting;

namespace Ows.Reporting.Tests;

/// <summary>
/// Tests the report generator skeleton.
/// </summary>
public sealed class OwsReportGeneratorTests
{
    /// <summary>
    /// Verifies the current placeholder response.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_ShouldReturnPlaceholderContent()
    {
        var generator = new OwsReportGenerator();

        var result = await generator.GenerateAsync(
            new ReportRequest
            {
                Format = ReportFormat.Html,
                VerificationResult = VerificationResult.Success("Verified")
            },
            CancellationToken.None);

        result.Format.Should().Be(ReportFormat.Html);
        result.Content.Should().Be("OWS report: not implemented yet");
    }
}
