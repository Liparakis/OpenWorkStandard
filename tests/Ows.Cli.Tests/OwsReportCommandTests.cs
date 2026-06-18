using FluentAssertions;
using Ows.Cli;

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
        File.WriteAllText(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(projectRoot);

            (await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync()).Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand().Parse(["watch"]).InvokeAsync()).Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand().Parse(["package"]).InvokeAsync()).Should().Be(0);

            var reportResult = await OwsCommandFactory.BuildRootCommand().Parse(["report"]).InvokeAsync();
            var reportPath = Path.Combine(projectRoot, $"{new DirectoryInfo(projectRoot).Name}.report.txt");

            reportResult.Should().Be(0);
            File.Exists(reportPath).Should().BeTrue();
            File.ReadAllText(reportPath).Should().Contain("Status: Success");
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
