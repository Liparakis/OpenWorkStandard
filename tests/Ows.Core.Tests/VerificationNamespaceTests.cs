using FluentAssertions;
using System.IO.Compression;
using System.Text.Json;
using Ows.Core.Events;
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
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(new OwsManifest { ProjectName = "sample", Platform = "Win32NT", TrackedPath = "sample" }));
                WriteEntry(archive, "timeline.jsonl", JsonSerializer.Serialize(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" }));
                WriteEntry(archive, "version_graph.json", "{\"nodes\":[],\"edges\":[]}");
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
                WriteEntry(archive, "version_graph.json", "{\"nodes\":[],\"edges\":[]}");
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
                WriteEntry(archive, "version_graph.json", "{\"nodes\":[],\"edges\":[]}");
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
                WriteEntry(archive, "version_graph.json", "{\"nodes\":[],\"edges\":[]}");
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

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
