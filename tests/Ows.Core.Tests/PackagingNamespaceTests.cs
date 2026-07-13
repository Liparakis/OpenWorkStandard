using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using Ows.Core.Events;
using Ows.Core.Notarization;
using Ows.Core.Packaging;
using Ows.Core.Verification;

namespace Ows.Core.Tests;

/// <summary>
/// Tests packaging types after consolidation into Ows.Core.
/// </summary>
public sealed class PackagingNamespaceTests {
    /// <summary>
    /// Verifies that package creation emits a real .owspkg archive with manifest and timeline content.
    /// </summary>
    [Fact]
    public async Task CreatePackageAsync_ShouldCreateArchiveWithManifestAndTimeline() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
        File.WriteAllText(Path.Combine(projectRoot, "src", "draft.txt"), "draft");
        var localFolder = Path.Combine(projectRoot, ".ows");
        Directory.CreateDirectory(localFolder);
        File.WriteAllText(Path.Combine(localFolder, "timeline.jsonl"), "{\"eventType\":\"FileCreated\"}");
        var outputPath = Path.Combine(projectRoot, "submission.owspkg");

        try {
            var builder = new OwsPackageBuilder();

            var result = await builder.CreatePackageAsync(
                new PackageCreationRequest {
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
        } finally {
            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that ignored paths are absent from the archive and manifest while text and binary artifacts remain available.
    /// </summary>
    [Fact]
    public async Task CreatePackageAsync_ShouldRespectIgnoreRulesAndKeepBinaryArtifactsOpaque() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-package-ignore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(projectRoot, "src"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "assets"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "bin"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "obj"));
        Directory.CreateDirectory(Path.Combine(projectRoot, ".git"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "node_modules", "dependency"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "logs"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "manual-output"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "scratch"));
        var localFolder = Path.Combine(projectRoot, ".ows");
        Directory.CreateDirectory(localFolder);

        File.WriteAllText(Path.Combine(projectRoot, ".owsignore"), "scratch/\n");
        File.WriteAllText(Path.Combine(projectRoot, "src", "main.cs"), "class Main {}\n");
        File.WriteAllBytes(Path.Combine(projectRoot, "assets", "data.bin"), [0, 1, 2, 255]);
        File.WriteAllText(Path.Combine(projectRoot, "bin", "generated.dll"), "generated");
        File.WriteAllText(Path.Combine(projectRoot, "obj", "generated.cache"), "generated");
        File.WriteAllText(Path.Combine(projectRoot, ".git", "index"), "git data");
        File.WriteAllText(Path.Combine(projectRoot, "node_modules", "dependency", "index.js"), "dependency");
        File.WriteAllText(Path.Combine(projectRoot, "logs", "run.log"), "log");
        File.WriteAllText(Path.Combine(projectRoot, "manual-output", "generated.txt"), "generated");
        File.WriteAllText(Path.Combine(projectRoot, "scratch", "notes.txt"), "ignored");
        File.WriteAllText(Path.Combine(projectRoot, ".env"), "SECRET=value");
        File.WriteAllText(Path.Combine(projectRoot, "secrets.json"), "{\"secret\":true}");
        File.WriteAllText(Path.Combine(localFolder, "config.json"),
            "{\"watcherSettings\":{\"excludeDirectories\":[\"manual-output\"]}}");
        File.WriteAllText(Path.Combine(localFolder, OwsConstants.TimelineFileName),
            JsonSerializer.Serialize(OwsEventChain.CreateChainedEvent(new OwsEvent {
                EventType = OwsEventType.FileCreated,
                ProjectId = "package-ignore-test",
                RelativePath = "src/main.cs"
            }, OwsEventChain.GenesisPreviousEventHash)) + Environment.NewLine);
        var outputPath = Path.Combine(projectRoot, "submission.owspkg");

        try {
            await new OwsPackageBuilder().CreatePackageAsync(new PackageCreationRequest {
                ProjectRootPath = projectRoot,
                OutputPackagePath = outputPath
            }, CancellationToken.None);

            using var archive = ZipFile.OpenRead(outputPath);
            var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
            entries.Should().Contain(["artifacts/.owsignore", "artifacts/src/main.cs", "artifacts/assets/data.bin"]);
            entries.Should().NotContain(entry => entry.Contains("bin/", StringComparison.OrdinalIgnoreCase));
            entries.Should().NotContain(entry => entry.Contains("obj/", StringComparison.OrdinalIgnoreCase));
            entries.Should().NotContain(entry => entry.Contains(".git/", StringComparison.OrdinalIgnoreCase));
            entries.Should().NotContain(entry => entry.Contains("node_modules/", StringComparison.OrdinalIgnoreCase));
            entries.Should().NotContain(entry => entry.Contains("logs/", StringComparison.OrdinalIgnoreCase));
            entries.Should().NotContain(entry => entry.Contains("manual-output/", StringComparison.OrdinalIgnoreCase));
            entries.Should().NotContain("artifacts/scratch/notes.txt");
            entries.Should().NotContain("artifacts/.env");
            entries.Should().NotContain("artifacts/secrets.json");

            using var manifestReader = new StreamReader(archive.GetEntry(OwsConstants.ManifestFileName)!.Open());
            using var manifest = JsonDocument.Parse(manifestReader.ReadToEnd());
            manifest.RootElement.GetProperty("ArtifactHashes").EnumerateObject()
                .Select(property => property.Name)
                .Should().BeEquivalentTo(["artifacts/.owsignore", "artifacts/assets/data.bin", "artifacts/src/main.cs"]);

            var verification = await new OwsPackageVerifier().VerifyAsync(new PackageVerificationRequest {
                PackagePath = outputPath
            }, CancellationToken.None);
            verification.IsSuccess.Should().BeTrue();
        } finally {
            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that package creation includes receipts when they exist locally.
    /// </summary>
    [Fact]
    public async Task CreatePackageAsync_ShouldIncludeReceiptsWhenAvailable() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-package-receipts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var localFolder = Path.Combine(projectRoot, ".ows");
        Directory.CreateDirectory(localFolder);
        File.WriteAllText(Path.Combine(localFolder, "timeline.jsonl"), "{\"eventType\":\"FileCreated\"}");
        File.WriteAllText(
            Path.Combine(localFolder, OwsConstants.ReceiptsFileName),
            JsonSerializer.Serialize(new ReceiptChain {
                SessionId = AssessmentSessionId.Create(),
                Receipts = []
            }));
        var outputPath = Path.Combine(projectRoot, "submission.owspkg");

        try {
            var builder = new OwsPackageBuilder();

            var result = await builder.CreatePackageAsync(
                new PackageCreationRequest {
                    ProjectRootPath = projectRoot,
                    OutputPackagePath = outputPath
                },
                CancellationToken.None);

            result.Created.Should().BeTrue();

            using var archive = ZipFile.OpenRead(outputPath);
            archive.GetEntry(OwsConstants.ReceiptsFileName).Should().NotBeNull();
        } finally {
            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that package creation includes session metadata and hashes it in the manifest.
    /// </summary>
    [Fact]
    public async Task CreatePackageAsync_ShouldIncludeSessionStateWhenAvailable() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-package-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var localFolder = Path.Combine(projectRoot, ".ows");
        Directory.CreateDirectory(localFolder);
        File.WriteAllText(Path.Combine(localFolder, "timeline.jsonl"), "{\"eventType\":\"FileCreated\"}");
        File.WriteAllText(
            Path.Combine(localFolder, OwsConstants.SessionFileName),
            JsonSerializer.Serialize(new SessionState {
                SessionId = "session-1",
                VerifierUrl = "https://verifier.test/"
            }));
        var outputPath = Path.Combine(projectRoot, "submission.owspkg");

        try {
            var builder = new OwsPackageBuilder();

            var result = await builder.CreatePackageAsync(
                new PackageCreationRequest {
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
        } finally {
            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }
}
