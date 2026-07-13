using System.Text.Json;
using FluentAssertions;
using Ows.Core;
using Ows.Core.Events;
using Ows.Core.Packaging;

namespace Ows.Cli.Tests;

/// <summary>
///     Represents the <see cref="ReviewerPackageArgumentTests" /> type.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class ReviewerPackageArgumentTests {
    /// <summary>
    ///     Verifies that the reviewer commands (verify, inspect, report) accept a package file path located outside the
    ///     current working directory.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ReviewerCommands_ShouldAcceptPackagePathOutsideCurrentDirectory() {
        var packageRoot = Path.Combine(Path.GetTempPath(), $"ows-review-package-{Guid.NewGuid():N}");
        var reviewerRoot = Path.Combine(Path.GetTempPath(), $"ows-reviewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(packageRoot, ".ows"));
        Directory.CreateDirectory(reviewerRoot);
        var originalDirectory = Directory.GetCurrentDirectory();
        var originalOut = Console.Out;
        try {
            var timelineEvent = OwsEventChain.CreateChainedEvent(
                new OwsEvent {
                    EventType = OwsEventType.FileCreated,
                    ProjectId = "review-fixture",
                    RelativePath = "README.md"
                }, OwsEventChain.GenesisPreviousEventHash
            );
            await File.WriteAllTextAsync(
                Path.Combine(packageRoot, ".ows", OwsConstants.TimelineFileName),
                JsonSerializer.Serialize(timelineEvent) + Environment.NewLine
            );
            var packagePath = Path.Combine(packageRoot, "review.owspkg");
            await OwsPackageBuilder.CreatePackageAsync(
                new PackageCreationRequest {
                    ProjectRootPath = packageRoot,
                    OutputPackagePath = packagePath
                }, CancellationToken.None
            );

            Directory.SetCurrentDirectory(reviewerRoot);
            Console.SetOut(new StringWriter());

            (await OwsCommandFactory.BuildRootCommand().Parse(["verify", packagePath]).InvokeAsync())
                .Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand().Parse(["inspect", packagePath, "--json"])
                                    .InvokeAsync())
                .Should().Be(0);
            (await OwsCommandFactory.BuildRootCommand()
                                    .Parse(["report", packagePath, "--format", "json"]).InvokeAsync())
                .Should().Be(0);

            File.Exists(Path.Combine(packageRoot, "review.report.json")).Should().BeTrue();
        } finally {
            Console.SetOut(originalOut);
            Directory.SetCurrentDirectory(originalDirectory);
            if (Directory.Exists(packageRoot)) {
                Directory.Delete(packageRoot, true);
            }

            if (Directory.Exists(reviewerRoot)) {
                Directory.Delete(reviewerRoot, true);
            }
        }
    }
}
