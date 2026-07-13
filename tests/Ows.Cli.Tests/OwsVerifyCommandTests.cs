using FluentAssertions;
using Ows.Core;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests the offline verify command.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class OwsVerifyCommandTests {
    [Fact]
    public async Task VerifyCommand_ShouldSucceedForCreatedPackage() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-verify-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();

        try {
            Directory.SetCurrentDirectory(projectRoot);
            (await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync()).Should().Be(0);
            await OwsTestHelpers.RunInitialScanAsync(projectRoot);
            (await OwsCommandFactory.BuildRootCommand().Parse(["package"]).InvokeAsync()).Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand().Parse(["verify"]).InvokeAsync()).Should().Be(0);
        } finally {
            Directory.SetCurrentDirectory(originalDirectory);
            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }
}
