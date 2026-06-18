using FluentAssertions;
using System.IO.Compression;
using Ows.Core.Packaging;

namespace Ows.Core.Tests;

/// <summary>
/// Tests packaging types after consolidation into Ows.Core.
/// </summary>
public sealed class PackagingNamespaceTests
{
    /// <summary>
    /// Verifies that package creation emits a real .owspkg archive with manifest and timeline content.
    /// </summary>
    [Fact]
    public async Task CreatePackageAsync_ShouldCreateArchiveWithManifestAndTimeline()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var localFolder = Path.Combine(projectRoot, ".ows");
        Directory.CreateDirectory(localFolder);
        File.WriteAllText(Path.Combine(localFolder, "timeline.jsonl"), "{\"eventType\":\"FileCreated\"}");
        var outputPath = Path.Combine(projectRoot, "submission.owspkg");

        try
        {
        var builder = new OwsPackageBuilder();

        var result = await builder.CreatePackageAsync(
            new PackageCreationRequest
            {
                ProjectRootPath = projectRoot,
                OutputPackagePath = outputPath
            },
            CancellationToken.None);

        result.Created.Should().BeTrue();
        File.Exists(outputPath).Should().BeTrue();

        using var archive = ZipFile.OpenRead(outputPath);
        archive.Entries.Select(entry => entry.FullName).Should().Contain(["manifest.json", "timeline.jsonl"]);
        archive.GetEntry("manifest.json")!.Open().Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }
}
