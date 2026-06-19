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
        result.Content.Should().Contain("Status: Unverified");
        result.Content.Should().Contain("OWS verify succeeded.");
        result.Content.Should().Contain("Findings:");
        result.Content.Should().Contain("Verification Scope:");
    }

    /// <summary>
    /// Verifies that the text report surfaces findings for human review.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_ShouldIncludeFindingsInTextReport()
    {
        var generator = new OwsReportGenerator();

        var result = await generator.GenerateAsync(
            new ReportRequest
            {
                Format = ReportFormat.Text,
                VerificationResult = VerificationResult.Success(
                    "OWS verify succeeded.",
                    TrustStatus.Unverified,
                    findings:
                    [
                        new VerificationFinding
                        {
                            Code = "receipt.chain.missing",
                            Severity = "Medium",
                            Title = "Receipt chain missing",
                            Detail = "No remote receipts were packaged.",
                            TechnicalDetail = "No receipts found.",
                            ReviewerAction = "Verify local-only mode."
                        }
                    ])
            },
            CancellationToken.None);

        result.Content.Should().Contain("[Medium] receipt.chain.missing");
        result.Content.Should().Contain("Receipt chain missing: No remote receipts were packaged.");
        result.Content.Should().Contain("Suggested Action: Verify local-only mode.");
        result.Content.Should().Contain("Technical Details: No receipts found.");
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
        document.RootElement.GetProperty("status").GetString().Should().Be("Verified");
        document.RootElement.GetProperty("summary").GetString().Should().Be("OWS verify succeeded.");
        document.RootElement.GetProperty("findings").EnumerateArray().Should().BeEmpty();
    }
}
