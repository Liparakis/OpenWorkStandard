using FluentAssertions;
using Ows.Core;
using Ows.Core.Events;
using Ows.Core.Packaging;

namespace Ows.Cli.Tests;

[Collection(CliCommandCollection.Name)]
public sealed class ReviewerPackageArgumentTests {
    [Fact]
    public async Task ReviewerCommands_ShouldAcceptPackagePathOutsideCurrentDirectory() {
        var packageRoot = Path.Combine(Path.GetTempPath(), $"ows-review-package-{Guid.NewGuid():N}");
        var reviewerRoot = Path.Combine(Path.GetTempPath(), $"ows-reviewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(packageRoot, ".ows"));
        Directory.CreateDirectory(reviewerRoot);
        var originalDirectory = Directory.GetCurrentDirectory();
        var originalOut = Console.Out;
        try {
            var timelineEvent = OwsEventChain.CreateChainedEvent(new OwsEvent {
                EventType = OwsEventType.FileCreated,
                ProjectId = "review-fixture",
                RelativePath = "README.md"
            }, OwsEventChain.GenesisPreviousEventHash);
            await File.WriteAllTextAsync(
                Path.Combine(packageRoot, ".ows", OwsConstants.TimelineFileName),
                System.Text.Json.JsonSerializer.Serialize(timelineEvent) + Environment.NewLine);
            var packagePath = Path.Combine(packageRoot, "review.owspkg");
            await new OwsPackageBuilder().CreatePackageAsync(new PackageCreationRequest {
                ProjectRootPath = packageRoot,
                OutputPackagePath = packagePath
            }, CancellationToken.None);

            Directory.SetCurrentDirectory(reviewerRoot);
            Console.SetOut(new StringWriter());

            (await Ows.Cli.OwsCommandFactory.BuildRootCommand().Parse(["verify", packagePath]).InvokeAsync())
                .Should().Be(0);
            (await Ows.Cli.OwsCommandFactory.BuildRootCommand().Parse(["inspect", packagePath, "--json"])
                    .InvokeAsync())
                .Should().Be(0);
            (await Ows.Cli.OwsCommandFactory.BuildRootCommand()
                    .Parse(["report", packagePath, "--format", "json"]).InvokeAsync())
                .Should().Be(0);

            File.Exists(Path.Combine(packageRoot, "review.report.json")).Should().BeTrue();
        } finally {
            Console.SetOut(originalOut);
            Directory.SetCurrentDirectory(originalDirectory);
            if (Directory.Exists(packageRoot)) {
                Directory.Delete(packageRoot, recursive: true);
            }

            if (Directory.Exists(reviewerRoot)) {
                Directory.Delete(reviewerRoot, recursive: true);
            }
        }
    }
}
