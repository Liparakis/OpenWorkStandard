using FluentAssertions;
using Ows.Cli;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests the verify command behavior.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class OwsVerifyCommandTests
{
    /// <summary>
    /// Verifies that the verify command succeeds for a package created by the CLI flow.
    /// </summary>
    [Fact]
    public async Task VerifyCommand_ShouldSucceedForCreatedPackage()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(projectRoot);

            (await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync()).Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand().Parse(["watch"]).InvokeAsync()).Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand().Parse(["package"]).InvokeAsync()).Should().Be(0);

            var verifyResult = await OwsCommandFactory.BuildRootCommand().Parse(["verify"]).InvokeAsync();

            verifyResult.Should().Be(0);
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
