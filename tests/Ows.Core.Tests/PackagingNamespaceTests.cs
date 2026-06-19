using FluentAssertions;
using System.IO.Compression;
using System.Text.Json;
using Ows.Core.Notarization;
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
        Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
        File.WriteAllText(Path.Combine(projectRoot, "src", "draft.txt"), "draft");
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
        archive.Entries.Select(entry => entry.FullName).Should().Contain(["manifest.json", "timeline.jsonl", "artifacts/src/draft.txt"]);
        archive.GetEntry("manifest.json")!.Open().Should().NotBeNull();

        using var manifestReader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
        using var manifestDocument = JsonDocument.Parse(manifestReader.ReadToEnd());
        manifestDocument.RootElement.GetProperty("TimelineHash").GetString().Should().NotBeNullOrWhiteSpace();
        manifestDocument.RootElement.GetProperty("VersionGraphHash").GetString().Should().NotBeNullOrWhiteSpace();
        manifestDocument.RootElement.GetProperty("ArtifactHashes").GetProperty("artifacts/src/draft.txt").GetString().Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that package creation includes receipts when they exist locally.
    /// </summary>
    [Fact]
    public async Task CreatePackageAsync_ShouldIncludeReceiptsWhenAvailable()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-package-receipts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var localFolder = Path.Combine(projectRoot, ".ows");
        Directory.CreateDirectory(localFolder);
        File.WriteAllText(Path.Combine(localFolder, "timeline.jsonl"), "{\"eventType\":\"FileCreated\"}");
        File.WriteAllText(
            Path.Combine(localFolder, OwsConstants.ReceiptsFileName),
            JsonSerializer.Serialize(new ReceiptChain
            {
                SessionId = AssessmentSessionId.Create(),
                Receipts = []
            }));
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

            using var archive = ZipFile.OpenRead(outputPath);
            archive.GetEntry(OwsConstants.ReceiptsFileName).Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that package creation includes session metadata and hashes it in the manifest.
    /// </summary>
    [Fact]
    public async Task CreatePackageAsync_ShouldIncludeSessionStateWhenAvailable()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-package-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var localFolder = Path.Combine(projectRoot, ".ows");
        Directory.CreateDirectory(localFolder);
        File.WriteAllText(Path.Combine(localFolder, "timeline.jsonl"), "{\"eventType\":\"FileCreated\"}");
        File.WriteAllText(
            Path.Combine(localFolder, OwsConstants.SessionFileName),
            JsonSerializer.Serialize(new SessionState
            {
                SessionId = "session-1",
                VerifierUrl = "https://verifier.test/"
            }));
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

            using var archive = ZipFile.OpenRead(outputPath);
            archive.GetEntry(OwsConstants.SessionFileName).Should().NotBeNull();

            using var manifestReader = new StreamReader(archive.GetEntry(OwsConstants.ManifestFileName)!.Open());
            using var manifestDocument = JsonDocument.Parse(manifestReader.ReadToEnd());
            manifestDocument.RootElement.GetProperty("SessionStateHash").GetString().Should().NotBeNullOrWhiteSpace();
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
