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
        result.Content.Should().Contain("Findings:");
        result.Content.Should().Contain("Review Signals:");
    }

    /// <summary>
    /// Verifies that the text report surfaces findings and review signals for human review.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_ShouldIncludeFindingsAndReviewSignalsInTextReport()
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
                            Code = "remote-receipts-missing",
                            Title = "Remote receipts missing",
                            Detail = "No remote receipts were packaged."
                        }
                    ],
                    reviewSignals:
                    [
                        new ReviewSignal
                        {
                            SignalType = ReviewSignalType.MissingHistory,
                            Title = "Missing history interval",
                            Detail = "A capture gap requires human review.",
                            Severity = 3
                        }
                    ])
            },
            CancellationToken.None);

        result.Content.Should().Contain("Remote receipts missing (remote-receipts-missing)");
        result.Content.Should().Contain("No remote receipts were packaged.");
        result.Content.Should().Contain("Missing history interval [MissingHistory] severity 3");
        result.Content.Should().Contain("A capture gap requires human review.");
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
        document.RootElement.GetProperty("findings").EnumerateArray().Should().BeEmpty();
        document.RootElement.GetProperty("reviewSignals").EnumerateArray().Should().BeEmpty();
    }
}
