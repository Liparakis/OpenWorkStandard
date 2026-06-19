using FluentAssertions;
using System.IO.Compression;
using Ows.Cli;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests the package command behavior.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class OwsPackageCommandTests
{
    /// <summary>
    /// Verifies that the package command emits a .owspkg archive from local OWS state.
    /// </summary>
    [Fact]
    public async Task PackageCommand_ShouldCreateOwsPackageInCurrentDirectory()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(projectRoot);

            (await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync()).Should().Be(0);
            await OwsTestHelpers.RunInitialScanAsync(projectRoot);

            var packageResult = await OwsCommandFactory.BuildRootCommand().Parse(["package"]).InvokeAsync();
            var packagePath = Path.Combine(projectRoot, $"{new DirectoryInfo(projectRoot).Name}.owspkg");

            packageResult.Should().Be(0);
            File.Exists(packagePath).Should().BeTrue();

            using var archive = ZipFile.OpenRead(packagePath);
            archive.Entries.Select(entry => entry.FullName).Should().Contain(["manifest.json", "timeline.jsonl", "artifacts/draft.txt"]);
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
