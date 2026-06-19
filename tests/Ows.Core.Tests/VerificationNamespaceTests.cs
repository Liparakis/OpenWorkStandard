using FluentAssertions;
using System.IO.Compression;
using System.Text.Json;
using Ows.Core.Events;
using Ows.Core.Graph;
using Ows.Core.Hashing;
using Ows.Core.Notarization;
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
            var timelineText = SerializeTimeline(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" });
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
        result.TrustStatus.Should().Be(TrustStatus.Unverified);
        result.Errors.Should().BeEmpty();
        result.Findings.Should().Contain(finding => finding.Code == "receipt.chain.missing");
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
    /// Verifies that valid packaged receipts upgrade trust to verified.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldReturnVerifiedWhenReceiptChainMatchesTimelineHead()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-receipts-{Guid.NewGuid():N}.owspkg");

        try
        {
            var hashService = new Sha256HashService();
            var timelineEvents = CreateChainedEvents(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" });
            var timelineText = string.Join(Environment.NewLine, timelineEvents.Select(owsEvent => JsonSerializer.Serialize(owsEvent)));
            var graphText = JsonSerializer.Serialize(WorkVersionGraph.CreateEmpty());
            var sessionId = AssessmentSessionId.Create();
            var receipt = ReceiptChainVerifier.IssueReceipt(
                new Checkpoint
                {
                    SessionId = sessionId,
                    SequenceNumber = 1,
                    TimelineHeadHash = timelineEvents[^1].EventHash
                },
                ReceiptChainVerifier.GenesisPreviousReceiptHash);
            var receiptChain = new ReceiptChain
            {
                SessionId = sessionId,
                Receipts = [receipt]
            };

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
                WriteEntry(archive, OwsConstants.ReceiptsFileName, JsonSerializer.Serialize(receiptChain));
            }

            var verifier = new OwsPackageVerifier();
            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.TrustStatus.Should().Be(TrustStatus.Verified);
            result.Findings.Should().Contain(finding => finding.Code == "timeline.chain.valid");
            result.Findings.Should().Contain(finding => finding.Code == "receipt.chain.valid");
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
    /// Verifies that mismatched receipt heads fail verification.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFailWhenReceiptChainHeadDoesNotMatchTimelineHead()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-receipts-mismatch-{Guid.NewGuid():N}.owspkg");

        try
        {
            var hashService = new Sha256HashService();
            var timelineEvents = CreateChainedEvents(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" });
            var timelineText = string.Join(Environment.NewLine, timelineEvents.Select(owsEvent => JsonSerializer.Serialize(owsEvent)));
            var graphText = JsonSerializer.Serialize(WorkVersionGraph.CreateEmpty());
            var sessionId = AssessmentSessionId.Create();
            var receipt = ReceiptChainVerifier.IssueReceipt(
                new Checkpoint
                {
                    SessionId = sessionId,
                    SequenceNumber = 1,
                    TimelineHeadHash = "wrong-head"
                },
                ReceiptChainVerifier.GenesisPreviousReceiptHash);
            var receiptChain = new ReceiptChain
            {
                SessionId = sessionId,
                Receipts = [receipt]
            };

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
                WriteEntry(archive, OwsConstants.ReceiptsFileName, JsonSerializer.Serialize(receiptChain));
            }

            var verifier = new OwsPackageVerifier();
            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.TrustStatus.Should().Be(TrustStatus.Invalid);
            result.Errors.Should().Contain(error => error.Contains("receipt chain head", StringComparison.OrdinalIgnoreCase));
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
    /// Verifies that verifier-backed verification rejects packaged receipts that differ from the trusted remote chain.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFailWhenPackagedReceiptChainDiffersFromTrustedRemoteChain()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-receipts-remote-mismatch-{Guid.NewGuid():N}.owspkg");

        try
        {
            var hashService = new Sha256HashService();
            var timelineEvents = CreateChainedEvents(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" });
            var timelineText = string.Join(Environment.NewLine, timelineEvents.Select(owsEvent => JsonSerializer.Serialize(owsEvent)));
            var graphText = JsonSerializer.Serialize(WorkVersionGraph.CreateEmpty());
            var sessionId = AssessmentSessionId.Create();
            var trustedReceipt = ReceiptChainVerifier.IssueReceipt(
                new Checkpoint
                {
                    SessionId = sessionId,
                    SequenceNumber = 1,
                    TimelineHeadHash = timelineEvents[^1].EventHash
                },
                ReceiptChainVerifier.GenesisPreviousReceiptHash);
            var packagedReceiptChain = new ReceiptChain
            {
                SessionId = sessionId,
                Receipts = [trustedReceipt with { TimelineHeadHash = "tampered-head" }]
            };
            var trustedReceiptChain = new ReceiptChain
            {
                SessionId = sessionId,
                Receipts = [trustedReceipt]
            };

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
                WriteEntry(archive, OwsConstants.ReceiptsFileName, JsonSerializer.Serialize(packagedReceiptChain));
            }

            var verifier = new OwsPackageVerifier();
            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest
                {
                    PackagePath = packagePath,
                    TrustedReceiptChain = trustedReceiptChain
                },
                CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.TrustStatus.Should().Be(TrustStatus.Invalid);
            result.Errors.Should().Contain(error => error.Contains("trusted remote receipt chain", StringComparison.OrdinalIgnoreCase));
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
    /// Verifies that verifier-backed verification can fall back to a trusted remote session head when packaged receipts are absent.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldReturnUnverifiedWhenTrustedRemoteHeadMatchesButReceiptsAreNotPackaged()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-remote-head-{Guid.NewGuid():N}.owspkg");

        try
        {
            var hashService = new Sha256HashService();
            var timelineEvents = CreateChainedEvents(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" });
            var timelineText = string.Join(Environment.NewLine, timelineEvents.Select(owsEvent => JsonSerializer.Serialize(owsEvent)));
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
                new PackageVerificationRequest
                {
                    PackagePath = packagePath,
                    TrustedSessionHead = new SessionHeadResponse
                    {
                        SessionId = "session-1",
                        LastSequenceNumber = 1,
                        LastTimelineHeadHash = timelineEvents[^1].EventHash,
                        LastReceiptHash = "receipt-1"
                    }
                },
                CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.TrustStatus.Should().Be(TrustStatus.Unverified);
            result.Findings.Should().Contain(finding => finding.Code == "receipt.chain.missing");
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
            result.TrustStatus.Should().Be(TrustStatus.Invalid);
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
                WriteEntry(archive, "timeline.jsonl", SerializeTimeline(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" }));
                WriteEntry(archive, "version_graph.json", JsonSerializer.Serialize(WorkVersionGraph.CreateEmpty()));
            }

            var verifier = new OwsPackageVerifier();

            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.TrustStatus.Should().Be(TrustStatus.Invalid);
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
            result.TrustStatus.Should().Be(TrustStatus.Invalid);
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
                WriteEntry(archive, "timeline.jsonl", SerializeTimeline(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" }));
                WriteEntry(archive, "version_graph.json", "{bad json");
            }

            var verifier = new OwsPackageVerifier();

            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.TrustStatus.Should().Be(TrustStatus.Invalid);
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
        File.WriteAllText(Path.Combine(localFolder, "timeline.jsonl"), SerializeTimeline(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" }));
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
            result.TrustStatus.Should().Be(TrustStatus.Invalid);
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
        File.WriteAllText(Path.Combine(localFolder, "timeline.jsonl"), SerializeTimeline(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" }));
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
            result.TrustStatus.Should().Be(TrustStatus.Invalid);
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
    /// Verifies that session metadata tampering after packaging fails hash validation.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFailWhenSessionStateHashDoesNotMatchManifest()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-verify-session-hash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var localFolder = Path.Combine(projectRoot, ".ows");
        Directory.CreateDirectory(localFolder);
        File.WriteAllText(Path.Combine(localFolder, "timeline.jsonl"), SerializeTimeline(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" }));
        File.WriteAllText(
            Path.Combine(localFolder, OwsConstants.SessionFileName),
            JsonSerializer.Serialize(new SessionState
            {
                SessionId = "session-1",
                VerifierUrl = "https://verifier.test/"
            }));
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
                archive.GetEntry(OwsConstants.SessionFileName)!.Delete();
                WriteEntry(archive, OwsConstants.SessionFileName, JsonSerializer.Serialize(new SessionState
                {
                    SessionId = "session-2",
                    VerifierUrl = "https://verifier.test/"
                }));
            }

            var verifier = new OwsPackageVerifier();
            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.TrustStatus.Should().Be(TrustStatus.Invalid);
            result.Errors.Should().Contain(error => error.Contains("session state hash", StringComparison.OrdinalIgnoreCase));
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
    /// Verifies that undeclared artifact entries fail verification.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFailWhenUnexpectedArtifactEntryExists()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-verify-extra-artifact-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "draft.txt"), "original");
        var localFolder = Path.Combine(projectRoot, ".ows");
        Directory.CreateDirectory(localFolder);
        File.WriteAllText(Path.Combine(localFolder, "timeline.jsonl"), SerializeTimeline(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" }));
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
                WriteEntry(archive, "artifacts/extra.txt", "surprise");
            }

            var verifier = new OwsPackageVerifier();

            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.TrustStatus.Should().Be(TrustStatus.Invalid);
            result.Errors.Should().Contain(error => error.Contains("unexpected artifact", StringComparison.OrdinalIgnoreCase));
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
    /// Verifies that a modified event fails chain verification.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFailWhenEventHashDoesNotMatchContent()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-chain-modified-{Guid.NewGuid():N}.owspkg");

        try
        {
            var events = CreateChainedEvents(
                new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample", RelativePath = "a.txt" },
                new OwsEvent { EventType = OwsEventType.FileModified, ProjectId = "sample", RelativePath = "a.txt" });
            var tamperedEvent = events[1] with { RelativePath = "b.txt" };
            var timelineText = string.Join(Environment.NewLine, JsonSerializer.Serialize(events[0]), JsonSerializer.Serialize(tamperedEvent));
            var graphText = JsonSerializer.Serialize(WorkVersionGraph.CreateEmpty());
            var hashService = new Sha256HashService();

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
            var result = await verifier.VerifyAsync(new PackageVerificationRequest { PackagePath = packagePath }, CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Contains("invalid event hash", StringComparison.OrdinalIgnoreCase));
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
    /// Verifies that a missing event fails chain verification.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFailWhenEventIsMissingFromChain()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-chain-missing-{Guid.NewGuid():N}.owspkg");

        try
        {
            var events = CreateChainedEvents(
                new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample", RelativePath = "a.txt" },
                new OwsEvent { EventType = OwsEventType.FileModified, ProjectId = "sample", RelativePath = "a.txt" },
                new OwsEvent { EventType = OwsEventType.FileModified, ProjectId = "sample", RelativePath = "b.txt" });
            var timelineText = string.Join(Environment.NewLine, JsonSerializer.Serialize(events[0]), JsonSerializer.Serialize(events[2]));
            var graphText = JsonSerializer.Serialize(WorkVersionGraph.CreateEmpty());
            var hashService = new Sha256HashService();

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
            var result = await verifier.VerifyAsync(new PackageVerificationRequest { PackagePath = packagePath }, CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Contains("broken event chain", StringComparison.OrdinalIgnoreCase));
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
    /// Verifies that a duplicated event fails chain verification.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFailWhenEventIsDuplicatedInChain()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-chain-duplicated-{Guid.NewGuid():N}.owspkg");

        try
        {
            var events = CreateChainedEvents(
                new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample", RelativePath = "a.txt" },
                new OwsEvent { EventType = OwsEventType.FileModified, ProjectId = "sample", RelativePath = "a.txt" });
            var timelineText = string.Join(Environment.NewLine, JsonSerializer.Serialize(events[0]), JsonSerializer.Serialize(events[1]), JsonSerializer.Serialize(events[1]));
            var graphText = JsonSerializer.Serialize(WorkVersionGraph.CreateEmpty());
            var hashService = new Sha256HashService();

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
            var result = await verifier.VerifyAsync(new PackageVerificationRequest { PackagePath = packagePath }, CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Contains("broken event chain", StringComparison.OrdinalIgnoreCase));
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
    /// Verifies that reordered events fail chain verification.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldFailWhenEventsAreReordered()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-chain-reordered-{Guid.NewGuid():N}.owspkg");

        try
        {
            var events = CreateChainedEvents(
                new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample", RelativePath = "a.txt" },
                new OwsEvent { EventType = OwsEventType.FileModified, ProjectId = "sample", RelativePath = "a.txt" });
            var timelineText = string.Join(Environment.NewLine, JsonSerializer.Serialize(events[1]), JsonSerializer.Serialize(events[0]));
            var graphText = JsonSerializer.Serialize(WorkVersionGraph.CreateEmpty());
            var hashService = new Sha256HashService();

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
            var result = await verifier.VerifyAsync(new PackageVerificationRequest { PackagePath = packagePath }, CancellationToken.None);

            result.IsSuccess.Should().BeFalse();
            result.Errors.Should().Contain(error => error.Contains("broken event chain", StringComparison.OrdinalIgnoreCase));
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

    /// <summary>
    /// Creates a newline-delimited timeline from chained events.
    /// </summary>
    /// <param name="events">The source events to chain.</param>
    /// <returns>The JSONL timeline text.</returns>
    private static string SerializeTimeline(params OwsEvent[] events) =>
        string.Join(Environment.NewLine, CreateChainedEvents(events).Select(owsEvent => JsonSerializer.Serialize(owsEvent)));

    /// <summary>
    /// Creates chained events using the canonical event hash format.
    /// </summary>
    /// <param name="events">The source events to chain.</param>
    /// <returns>The chained event sequence.</returns>
    private static IReadOnlyList<OwsEvent> CreateChainedEvents(params OwsEvent[] events)
    {
        var chainedEvents = new List<OwsEvent>(events.Length);
        var previousEventHash = OwsEventChain.GenesisPreviousEventHash;

        foreach (var owsEvent in events)
        {
            var chainedEvent = OwsEventChain.CreateChainedEvent(owsEvent, previousEventHash);
            chainedEvents.Add(chainedEvent);
            previousEventHash = chainedEvent.EventHash;
        }

        return chainedEvents;
    }

    /// <summary>
    /// Verifies that OwsPackageVerifier trust grading logic handles session lease gaps and work after lease expiration correctly.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldApplyTrustGradingForLeaseGapsAndWorkAfterLease()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-leases-{Guid.NewGuid():N}.owspkg");

        try
        {
            var hashService = new Sha256HashService();
            var eventTime = DateTimeOffset.Parse("2026-06-19T12:00:00Z");
            var timelineEvents = CreateChainedEvents(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample", TimestampUtc = eventTime });
            var timelineText = string.Join(Environment.NewLine, timelineEvents.Select(owsEvent => JsonSerializer.Serialize(owsEvent)));
            var graphText = JsonSerializer.Serialize(WorkVersionGraph.CreateEmpty());
            var sessionId = AssessmentSessionId.Create();
            var receipt = ReceiptChainVerifier.IssueReceipt(
                new Checkpoint
                {
                    SessionId = sessionId,
                    SequenceNumber = 1,
                    TimelineHeadHash = timelineEvents[^1].EventHash
                },
                ReceiptChainVerifier.GenesisPreviousReceiptHash);
            var receiptChain = new ReceiptChain
            {
                SessionId = sessionId,
                Receipts = [receipt]
            };

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
                WriteEntry(archive, OwsConstants.ReceiptsFileName, JsonSerializer.Serialize(receiptChain));
            }

            var verifier = new OwsPackageVerifier();

            // Case A: Short lease gap (e.g., 120 seconds, limit is 300) -> Degraded + lease.gap.short
            {
                var request = new PackageVerificationRequest
                {
                    PackagePath = packagePath,
                    TrustedReceiptChain = receiptChain,
                    TrustedSessionHead = new SessionHeadResponse
                    {
                        SessionId = sessionId.Value,
                        LastSequenceNumber = 1,
                        LastTimelineHeadHash = timelineEvents[^1].EventHash,
                        LastReceiptHash = receipt.ReceiptHash
                    },
                    SessionHasLeaseGap = true,
                    SessionMaxLeaseGapSeconds = 120,
                    SignificantGapSeconds = 300
                };
                var result = await verifier.VerifyAsync(request, CancellationToken.None);
                result.IsSuccess.Should().BeTrue();
                result.TrustStatus.Should().Be(TrustStatus.Degraded);
                result.Findings.Should().Contain(f => f.Code == "lease.gap.short");
            }

            // Case B: Significant lease gap (e.g., 600 seconds, limit is 300) -> Unverified + lease.gap.long
            {
                var request = new PackageVerificationRequest
                {
                    PackagePath = packagePath,
                    TrustedReceiptChain = receiptChain,
                    TrustedSessionHead = new SessionHeadResponse
                    {
                        SessionId = sessionId.Value,
                        LastSequenceNumber = 1,
                        LastTimelineHeadHash = timelineEvents[^1].EventHash,
                        LastReceiptHash = receipt.ReceiptHash
                    },
                    SessionHasLeaseGap = true,
                    SessionMaxLeaseGapSeconds = 600,
                    SignificantGapSeconds = 300
                };
                var result = await verifier.VerifyAsync(request, CancellationToken.None);
                result.IsSuccess.Should().BeTrue();
                result.TrustStatus.Should().Be(TrustStatus.Unverified);
                result.Findings.Should().Contain(f => f.Code == "lease.gap.long");
            }

            // Case C: Work after lease expiration (short delay e.g., 10 seconds) -> Degraded + lease.work_after_expiration
            {
                var request = new PackageVerificationRequest
                {
                    PackagePath = packagePath,
                    TrustedReceiptChain = receiptChain,
                    TrustedSessionHead = new SessionHeadResponse
                    {
                        SessionId = sessionId.Value,
                        LastSequenceNumber = 1,
                        LastTimelineHeadHash = timelineEvents[^1].EventHash,
                        LastReceiptHash = receipt.ReceiptHash
                    },
                    SessionLeaseExpiresAt = eventTime.AddSeconds(-10),
                    SignificantGapSeconds = 300
                };
                var result = await verifier.VerifyAsync(request, CancellationToken.None);
                result.IsSuccess.Should().BeTrue();
                result.TrustStatus.Should().Be(TrustStatus.Degraded);
                result.Findings.Should().Contain(f => f.Code == "lease.work_after_expiration");
            }

            // Case D: Work after lease expiration (long delay e.g., 600 seconds) -> Unverified + lease.work_after_expiration
            {
                var request = new PackageVerificationRequest
                {
                    PackagePath = packagePath,
                    TrustedReceiptChain = receiptChain,
                    TrustedSessionHead = new SessionHeadResponse
                    {
                        SessionId = sessionId.Value,
                        LastSequenceNumber = 1,
                        LastTimelineHeadHash = timelineEvents[^1].EventHash,
                        LastReceiptHash = receipt.ReceiptHash
                    },
                    SessionLeaseExpiresAt = eventTime.AddSeconds(-600),
                    SignificantGapSeconds = 300
                };
                var result = await verifier.VerifyAsync(request, CancellationToken.None);
                result.IsSuccess.Should().BeTrue();
                result.TrustStatus.Should().Be(TrustStatus.Unverified);
                result.Findings.Should().Contain(f => f.Code == "lease.work_after_expiration");
            }
        }
        finally
        {
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }
        }
    }
}
