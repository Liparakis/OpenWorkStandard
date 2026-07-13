using System.Text.Json;
using FluentAssertions;
using Ows.Core;
using Ows.Core.Events;
using Ows.Core.Packaging;

namespace Ows.Cli.Tests;

/// <summary>
///     Tests the local reviewer inspection command.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class OwsInspectCommandTests {
    /// <summary>
    ///     Verifies that the inspect command exposes a local package summary as JSON when executed with the --json flag.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task InspectCommand_ShouldExposeLocalPackageSummaryAsJson() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-inspect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(projectRoot, ".ows"));
        var originalDirectory = Directory.GetCurrentDirectory();
        var originalOut = Console.Out;
        try {
            var timelineEvent = OwsEventChain.CreateChainedEvent(
                new OwsEvent {
                    EventType = OwsEventType.FileCreated,
                    ProjectId = "inspect-fixture",
                    RelativePath = "README.md"
                }, OwsEventChain.GenesisPreviousEventHash
            );
            File.WriteAllText(
                Path.Combine(projectRoot, ".ows", OwsConstants.TimelineFileName),
                JsonSerializer.Serialize(timelineEvent) + Environment.NewLine
            );
            var packagePath = Path.Combine(projectRoot, "inspection.owspkg");
            await OwsPackageBuilder.CreatePackageAsync(
                new PackageCreationRequest {
                    ProjectRootPath = projectRoot,
                    OutputPackagePath = packagePath
                }, CancellationToken.None
            );

            Directory.SetCurrentDirectory(projectRoot);
            await using var output = new StringWriter();
            Console.SetOut(output);
            var exitCode = await OwsCommandFactory.BuildRootCommand().Parse(
                ["inspect", "--package-path", packagePath, "--json"]
            ).InvokeAsync();

            exitCode.Should().Be(0);
            output.ToString().Should().Contain("\"signatureStatus\"");
            output.ToString().Should().Contain("\"packageRootHash\"");
        } finally {
            Console.SetOut(originalOut);
            Directory.SetCurrentDirectory(originalDirectory);
            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, true);
            }
        }
    }
}
