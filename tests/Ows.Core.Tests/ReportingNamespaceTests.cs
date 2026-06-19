using FluentAssertions;
using Ows.Core.Reporting;
using Ows.Core.Verification;
using System.Text.Json;

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
                VerificationResult = VerificationResult.Success("OWS verify succeeded.", TrustStatus.Unverified)
            },
            CancellationToken.None);

        result.Format.Should().Be(ReportFormat.Text);
        result.Content.Should().Contain("Status: Success");
        result.Content.Should().Contain("Trust: Unverified");
        result.Content.Should().Contain("OWS verify succeeded.");
    }

    /// <summary>
    /// Verifies that the report generator can emit JSON output.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_ShouldReturnJsonSummary()
    {
        var generator = new OwsReportGenerator();

        var result = await generator.GenerateAsync(
            new ReportRequest
            {
                Format = ReportFormat.Json,
                VerificationResult = VerificationResult.Success("OWS verify succeeded.", TrustStatus.Verified)
            },
            CancellationToken.None);

        using var document = JsonDocument.Parse(result.Content);
        result.Format.Should().Be(ReportFormat.Json);
        document.RootElement.GetProperty("status").GetString().Should().Be("Success");
        document.RootElement.GetProperty("trust").GetString().Should().Be("Verified");
        document.RootElement.GetProperty("summary").GetString().Should().Be("OWS verify succeeded.");
        document.RootElement.GetProperty("errors").EnumerateArray().Should().ContainSingle()
            .Which.GetString().Should().Be("None");
    }
}
