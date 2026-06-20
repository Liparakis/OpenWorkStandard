using FluentAssertions;
using System.Text.Json;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests the report command behavior.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class OwsReportCommandTests
{
    /// <summary>
    /// Verifies that the report command writes a text report for a created package.
    /// </summary>
    [Fact]
    public async Task ReportCommand_ShouldWriteTextReportInCurrentDirectory()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(projectRoot);

            (await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync()).Should().Be(0);
            await OwsTestHelpers.RunInitialScanAsync(projectRoot);
            (await OwsCommandFactory.BuildRootCommand().Parse(["package"]).InvokeAsync()).Should().Be(0);

            var reportResult = await OwsCommandFactory.BuildRootCommand().Parse(["report"]).InvokeAsync();
            var reportPath = Path.Combine(projectRoot, $"{new DirectoryInfo(projectRoot).Name}.report.txt");

            reportResult.Should().Be(0);
            File.Exists(reportPath).Should().BeTrue();
            (await File.ReadAllTextAsync(reportPath)).Should().Contain("Status: Unverified");
            (await File.ReadAllTextAsync(reportPath)).Should().Contain("Findings:");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that the report command can write a JSON report for a created package.
    /// </summary>
    [Fact]
    public async Task ReportCommand_ShouldWriteJsonReportInCurrentDirectory()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-report-json-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(projectRoot);

            (await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync()).Should().Be(0);
            await OwsTestHelpers.RunInitialScanAsync(projectRoot);
            (await OwsCommandFactory.BuildRootCommand().Parse(["package"]).InvokeAsync()).Should().Be(0);

            var reportResult = await OwsCommandFactory.BuildRootCommand().Parse(["report", "--format", "json"])
                .InvokeAsync();
            var reportPath = Path.Combine(projectRoot, $"{new DirectoryInfo(projectRoot).Name}.report.json");

            reportResult.Should().Be(0);
            File.Exists(reportPath).Should().BeTrue();
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            document.RootElement.GetProperty("status").GetString().Should().Be("Unverified");
            document.RootElement.GetProperty("findings").EnumerateArray().Should().NotBeEmpty();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }
}