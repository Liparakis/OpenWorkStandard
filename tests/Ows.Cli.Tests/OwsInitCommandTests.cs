using FluentAssertions;
using Ows.Cli;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests the init command behavior.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class OwsInitCommandTests
{
    /// <summary>
    /// Verifies that the init command creates local OWS state in the current directory.
    /// </summary>
    [Fact]
    public async Task InitCommand_ShouldCreateLocalOwsStateInCurrentDirectory()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-init-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(projectRoot);

            var parseResult = OwsCommandFactory.BuildRootCommand().Parse(["init"]);
            var exitCode = await parseResult.InvokeAsync();

            exitCode.Should().Be(0);
            Directory.Exists(Path.Combine(projectRoot, ".ows")).Should().BeTrue();
            File.Exists(Path.Combine(projectRoot, ".ows", "config.json")).Should().BeTrue();
            File.Exists(Path.Combine(projectRoot, ".ows", "timeline.jsonl")).Should().BeTrue();
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
