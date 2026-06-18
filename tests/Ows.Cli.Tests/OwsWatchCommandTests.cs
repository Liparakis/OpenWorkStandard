using FluentAssertions;
using Ows.Cli;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests the watch command behavior.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class OwsWatchCommandTests
{
    /// <summary>
    /// Verifies that the watch command appends file events to the local timeline.
    /// </summary>
    [Fact]
    public async Task WatchCommand_ShouldAppendExistingFilesToTimeline()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(projectRoot);

            var initResult = await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync();
            initResult.Should().Be(0);

            var watchResult = await OwsCommandFactory.BuildRootCommand().Parse(["watch"]).InvokeAsync();
            var timelinePath = Path.Combine(projectRoot, ".ows", "timeline.jsonl");
            var lines = File.ReadAllLines(timelinePath);

            watchResult.Should().Be(0);
            lines.Should().Contain(line => line.Contains("draft.txt"));
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
