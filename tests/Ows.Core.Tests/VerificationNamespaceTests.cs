using FluentAssertions;
using System.IO.Compression;
using System.Text.Json;
using Ows.Core.Events;
using Ows.Core.Graph;
using Ows.Core.Hashing;
using Ows.Core.Packaging;
using Ows.Core.Verification;

namespace Ows.Core.Tests;

/// <summary>
/// Tests verification types after consolidation into Ows.Core.
/// </summary>
public sealed class VerificationNamespaceTests
{
    /// <summary>
    /// Verifies that a package with required entries passes verification.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldSucceedWhenRequiredEntriesExist()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-{Guid.NewGuid():N}.owspkg");

        try
        {
            var hashService = new Sha256HashService();
            var timelineText = JsonSerializer.Serialize(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" });
            var graphText = JsonSerializer.Serialize(WorkVersionGraph.CreateEmpty());
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new OwsManifest
                {
                    ProjectName = "sample",
                    Platform = "Win32NT",
                    TrackedPath = "sample",
                    TimelineHash = hashService.ComputeHash(timelineText),
                    VersionGraphHash = hashService.ComputeHash(graphText),
                    ArtifactHashes = new Dictionary<string, string>()
                }));
                WriteEntry(archive, "timeline.jsonl", timelineText);
                WriteEntry(archive, "version_graph.json", graphText);
            }

        var verifier = new OwsPackageVerifier();

        var result = await verifier.VerifyAsync(
            new PackageVerificationRequest { PackagePath = packagePath },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }

    /// <summary>
    /// Verifies that a package missing required entries fails verification.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFailWhenTimelineIsMissing()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-missing-{Guid.NewGuid():N}.owspkg");

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new OwsManifest { ProjectName = "sample", Platform = "Win32NT", TrackedPath = "sample" }));
                WriteEntry(archive, "version_graph.json", JsonSerializer.Serialize(WorkVersionGraph.CreateEmpty()));
            }

            var verifier = new OwsPackageVerifier();

            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Contains("timeline.jsonl"));
        }
        finally
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }

    /// <summary>
    /// Verifies that invalid manifest JSON fails verification.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFailWhenManifestJsonIsInvalid()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-bad-manifest-{Guid.NewGuid():N}.owspkg");

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "manifest.json", "{bad json");
                WriteEntry(archive, "timeline.jsonl", JsonSerializer.Serialize(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" }));
                WriteEntry(archive, "version_graph.json", JsonSerializer.Serialize(WorkVersionGraph.CreateEmpty()));
            }

            var verifier = new OwsPackageVerifier();

            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Contains("manifest.json"));
        }
        finally
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }

    /// <summary>
    /// Verifies that invalid timeline JSONL fails verification.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFailWhenTimelineJsonlIsInvalid()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-bad-timeline-{Guid.NewGuid():N}.owspkg");

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new OwsManifest { ProjectName = "sample", Platform = "Win32NT", TrackedPath = "sample" }));
                WriteEntry(archive, "timeline.jsonl", "{bad json");
                WriteEntry(archive, "version_graph.json", JsonSerializer.Serialize(WorkVersionGraph.CreateEmpty()));
            }

            var verifier = new OwsPackageVerifier();

            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Contains("timeline.jsonl"));
        }
        finally
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }

    /// <summary>
    /// Verifies that invalid version graph JSON fails verification.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFailWhenVersionGraphJsonIsInvalid()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-bad-graph-{Guid.NewGuid():N}.owspkg");

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new OwsManifest { ProjectName = "sample", Platform = "Win32NT", TrackedPath = "sample" }));
                WriteEntry(archive, "timeline.jsonl", JsonSerializer.Serialize(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" }));
                WriteEntry(archive, "version_graph.json", "{bad json");
            }

            var verifier = new OwsPackageVerifier();

            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Contains("version_graph.json"));
        }
        finally
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }

    /// <summary>
    /// Verifies that timeline tampering after packaging fails hash validation.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFailWhenTimelineHashDoesNotMatchManifest()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-verify-hash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var localFolder = Path.Combine(projectRoot, ".ows");
        Directory.CreateDirectory(localFolder);
        File.WriteAllText(Path.Combine(localFolder, "timeline.jsonl"), JsonSerializer.Serialize(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" }));
        var packagePath = Path.Combine(projectRoot, "submission.owspkg");

        try
        {
            var builder = new OwsPackageBuilder();
            await builder.CreatePackageAsync(
                new PackageCreationRequest
                {
                    ProjectRootPath = projectRoot,
                    OutputPackagePath = packagePath
                },
                CancellationToken.None);

            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
            {
                archive.GetEntry("timeline.jsonl")!.Delete();
                WriteEntry(archive, "timeline.jsonl", "{\"tampered\":true}");
            }

            var verifier = new OwsPackageVerifier();

            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Contains("timeline hash", StringComparison.OrdinalIgnoreCase));
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
    /// Verifies that artifact tampering after packaging fails hash validation.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFailWhenArtifactHashDoesNotMatchManifest()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-verify-artifact-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "draft.txt"), "original");
        var localFolder = Path.Combine(projectRoot, ".ows");
        Directory.CreateDirectory(localFolder);
        File.WriteAllText(Path.Combine(localFolder, "timeline.jsonl"), JsonSerializer.Serialize(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" }));
        var packagePath = Path.Combine(projectRoot, "submission.owspkg");

        try
        {
            var builder = new OwsPackageBuilder();
            await builder.CreatePackageAsync(
                new PackageCreationRequest
                {
                    ProjectRootPath = projectRoot,
                    OutputPackagePath = packagePath
                },
                CancellationToken.None);

            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
            {
                archive.GetEntry("artifacts/draft.txt")!.Delete();
                WriteEntry(archive, "artifacts/draft.txt", "tampered");
            }

            var verifier = new OwsPackageVerifier();

            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Contains("artifact hash", StringComparison.OrdinalIgnoreCase));
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
    /// Writes a text entry into the target archive for test setup.
    /// </summary>
    /// <param name="archive">The archive to update.</param>
    /// <param name="entryName">The entry path to create.</param>
    /// <param name="content">The text content to write.</param>
    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
