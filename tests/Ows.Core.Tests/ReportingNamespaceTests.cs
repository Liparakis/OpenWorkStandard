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
    /// Verifies the report generator skeleton still reports an unimplemented state.
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
