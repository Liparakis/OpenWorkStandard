using System.Text.Json;
using FluentAssertions;
using Ows.Core.Reporting;
using Ows.Core.Verification;

namespace Ows.Core.Tests;

/// <summary>
/// Tests reporting types after consolidation into Ows.Core.
/// </summary>
public sealed class ReportingNamespaceTests {
    /// <summary>
    /// Verifies that the report generator emits a useful text summary.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_ShouldReturnTextSummary() {
        var generator = new OwsReportGenerator();

        var result = await OwsReportGenerator.GenerateAsync(
            new ReportRequest {
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
    public async Task GenerateAsync_ShouldIncludeFindingsInTextReport() {
        var generator = new OwsReportGenerator();

        var result = await OwsReportGenerator.GenerateAsync(
            new ReportRequest {
                Format = ReportFormat.Text,
                VerificationResult = VerificationResult.Success(
                    "OWS verify succeeded.",
                    TrustStatus.Unverified,
                    findings:
                    [
                        new VerificationFinding
                        {
                            Code = "observation.gap",
                            Severity = "Medium",
                            Title = "Observation gap",
                            Detail = "The Agent was not observing the project for an interval.",
                            TechnicalDetail = "An observation gap was recorded in the local timeline.",
                            ReviewerAction = "Review the interval manually."
                        }
                    ])
            },
            CancellationToken.None);

        result.Content.Should().Contain("[Medium] observation.gap");
        result.Content.Should().Contain("Observation gap: The Agent was not observing the project for an interval.");
        result.Content.Should().Contain("Suggested Action: Review the interval manually.");
        result.Content.Should().Contain("Technical Details: An observation gap was recorded in the local timeline.");
    }

    /// <summary>
    /// Verifies that the report generator can emit JSON output.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_ShouldReturnJsonSummary() {
        var generator = new OwsReportGenerator();

        var result = await OwsReportGenerator.GenerateAsync(
            new ReportRequest {
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
