using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ows.Core.Agent;
using Ows.Core.Events;
using Ows.Core.Hashing;
using Ows.Core.Init;
using Ows.Core.Graph;
using Ows.Core.Notarization;
using Ows.Core.Packaging;
using Ows.Core.Verification;
using Xunit;

namespace Ows.Core.Tests;

public sealed class ObservationGapTests
{
    private static void EnsureCleanDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            try { Directory.Delete(path, recursive: true); } catch {}
        }
        Directory.CreateDirectory(path);
    }

    [Fact]
    public async Task GapDuration_UsesLastSnapshotOrHeartbeatTime()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-gap-dur-{Guid.NewGuid():N}");
        EnsureCleanDirectory(projectRoot);

        try
        {
            var localOws = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            Directory.CreateDirectory(localOws);

            var timelinePath = Path.Combine(localOws, OwsConstants.TimelineFileName);
            var snapshotPath = Path.Combine(localOws, OwsConstants.ObservedSnapshotFileName);
            var sessionPath = Path.Combine(localOws, OwsConstants.SessionFileName);

            // Write initial snapshot
            var baseTime = DateTimeOffset.UtcNow.AddMinutes(-30);
            var snapshot = new ObservedSnapshot
            {
                ObservedAt = baseTime,
                Files = new Dictionary<string, ObservedFileState>
                {
                    { "work.txt", new ObservedFileState { RelativePath = "work.txt", FileHash = "h1", Size = 10, LineCount = 1, ObservedAt = baseTime } }
                }
            };
            File.WriteAllText(snapshotPath, JsonSerializer.Serialize(snapshot));

            // Write session file with heartbeat 10 minutes later than snapshot (so 20 minutes ago)
            var heartbeatTime = baseTime.AddMinutes(10);
            var sessionState = new
            {
                sessionId = "test-session",
                lastHeartbeatAt = heartbeatTime.ToString("o")
            };
            File.WriteAllText(sessionPath, JsonSerializer.Serialize(sessionState));

            // Setup a mock event in timeline so chain is initialized
            var initialEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
            {
                EventType = OwsEventType.FileCreated,
                ProjectId = "test-project",
                RelativePath = "work.txt"
            }, OwsEventChain.GenesisPreviousEventHash);
            File.WriteAllText(timelinePath, JsonSerializer.Serialize(initialEvent) + Environment.NewLine);

            // Modify the file now to trigger change comparison
            var currFile = Path.Combine(projectRoot, "work.txt");
            File.WriteAllText(currFile, "modified content!"); // Size changed from 10 to 17

            var agent = new LocalTrackingAgent(NullLogger<LocalTrackingAgent>.Instance);
            var cts = new CancellationTokenSource();
            
            // Run Prepare and let recovery scan run during Start
            await agent.PrepareAsync(new TrackingAgentOptions
            {
                ProjectRootPath = projectRoot,
                DatabasePath = Path.Combine(localOws, "ows.db"),
                WasInterrupted = false
            }, cts.Token);

            // StartAsync will block in WatchAsync, so cancel after a brief moment
            cts.CancelAfter(200);
            try { await agent.StartAsync(cts.Token); } catch (OperationCanceledException) {}

            // Read timeline
            var lines = File.ReadAllLines(timelinePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            lines.Length.Should().Be(4);

            var gapEvent = JsonSerializer.Deserialize<OwsEvent>(lines[2]);
            gapEvent.Should().NotBeNull();
            gapEvent!.EventType.Should().Be(OwsEventType.ObservationGapDetected);
            
            gapEvent.Metadata.TryGetValue("gapStartedAt", out var startStr).Should().BeTrue();
            DateTimeOffset.Parse(startStr!).Should().BeCloseTo(heartbeatTime, TimeSpan.FromSeconds(5));

            gapEvent.Metadata.TryGetValue("gapDurationMs", out var durStr).Should().BeTrue();
            long.Parse(durStr!).Should().BeCloseTo((long)(DateTimeOffset.UtcNow - heartbeatTime).TotalMilliseconds, 10000);
        }
        finally
        {
            EnsureCleanDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task CleanStop_Restart_LargeChange_ReportsCleanStopped()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-clean-stop-{Guid.NewGuid():N}");
        EnsureCleanDirectory(projectRoot);

        try
        {
            var localOws = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            Directory.CreateDirectory(localOws);
            var timelinePath = Path.Combine(localOws, OwsConstants.TimelineFileName);
            var snapshotPath = Path.Combine(localOws, OwsConstants.ObservedSnapshotFileName);

            // Write initial snapshot and a timeline ending with WatcherStopped
            var baseTime = DateTimeOffset.UtcNow.AddMinutes(-5);
            var snapshot = new ObservedSnapshot
            {
                ObservedAt = baseTime,
                Files = new Dictionary<string, ObservedFileState>()
            };
            File.WriteAllText(snapshotPath, JsonSerializer.Serialize(snapshot));

            var startedEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
            {
                EventType = OwsEventType.WatcherStarted,
                ProjectId = "test-project"
            }, OwsEventChain.GenesisPreviousEventHash);
            var stoppedEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
            {
                EventType = OwsEventType.WatcherStopped,
                ProjectId = "test-project"
            }, startedEvent.EventHash);

            File.WriteAllText(timelinePath, JsonSerializer.Serialize(startedEvent) + Environment.NewLine + JsonSerializer.Serialize(stoppedEvent) + Environment.NewLine);

            // Add a large file (exceeds 50KB default byte threshold)
            var largeFile = Path.Combine(projectRoot, "large.txt");
            var largeContent = new string('A', 60000);
            File.WriteAllText(largeFile, largeContent);

            var agent = new LocalTrackingAgent(NullLogger<LocalTrackingAgent>.Instance);
            var cts = new CancellationTokenSource();

            await agent.PrepareAsync(new TrackingAgentOptions
            {
                ProjectRootPath = projectRoot,
                DatabasePath = Path.Combine(localOws, "ows.db"),
                WasInterrupted = false
            }, cts.Token);

            cts.CancelAfter(200);
            try { await agent.StartAsync(cts.Token); } catch (OperationCanceledException) {}

            var lines = File.ReadAllLines(timelinePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            lines.Length.Should().Be(5);

            var gapEvent = JsonSerializer.Deserialize<OwsEvent>(lines[3]);
            gapEvent!.EventType.Should().Be(OwsEventType.ObservationGapDetected);
            gapEvent.Metadata["previousState"].Should().Be("CleanStopped");
            gapEvent.Metadata["recoveryReason"].Should().Be("user_start");

            var largeEvent = JsonSerializer.Deserialize<OwsEvent>(lines[4]);
            largeEvent!.EventType.Should().Be(OwsEventType.LargeUnobservedChangeDetected);
            largeEvent.Metadata["changeKind"].Should().Be("Created");
            largeEvent.Metadata["relativePath"].Should().Be("large.txt");
        }
        finally
        {
            EnsureCleanDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task StalePid_EmitsWatcherInterrupted_NotWatcherStopped()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-stale-pid-{Guid.NewGuid():N}");
        EnsureCleanDirectory(projectRoot);

        try
        {
            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);

            var localOws = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            var watcherJsonPath = Path.Combine(localOws, "watcher.json");

            // Write fake watcher.json with stale/dead PID
            var state = new WatcherProcessState
            {
                Pid = 999999, // stale PID
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            };
            File.WriteAllText(watcherJsonPath, JsonSerializer.Serialize(state));

            // Call StopWatcherAsync (performs stale PID cleanup)
            await manager.StopWatcherAsync(projectRoot);

            // watcher.json must be deleted
            File.Exists(watcherJsonPath).Should().BeFalse();

            // Timeline must contain WatcherInterrupted but NOT WatcherStopped
            var timelinePath = Path.Combine(localOws, OwsConstants.TimelineFileName);
            File.Exists(timelinePath).Should().BeTrue();

            var lines = File.ReadAllLines(timelinePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            var eventTypes = lines.Select(l => JsonSerializer.Deserialize<OwsEvent>(l)!.EventType).ToList();

            eventTypes.Should().Contain(OwsEventType.WatcherInterrupted);
            eventTypes.Should().NotContain(OwsEventType.WatcherStopped);
        }
        finally
        {
            EnsureCleanDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task FirstBaseline_DoesNotCreateMisleadingOrdinaryFileCreatedEvidence()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-baseline-{Guid.NewGuid():N}");
        EnsureCleanDirectory(projectRoot);

        try
        {
            var file1 = Path.Combine(projectRoot, "test.txt");
            File.WriteAllText(file1, "some content");

            var agent = new LocalTrackingAgent(NullLogger<LocalTrackingAgent>.Instance);
            var cts = new CancellationTokenSource();
            var localOws = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            Directory.CreateDirectory(localOws);
            var timelinePath = Path.Combine(localOws, OwsConstants.TimelineFileName);

            await agent.PrepareAsync(new TrackingAgentOptions
            {
                ProjectRootPath = projectRoot,
                DatabasePath = Path.Combine(localOws, "ows.db"),
                WasInterrupted = false
            }, cts.Token);

            cts.CancelAfter(200);
            try { await agent.StartAsync(cts.Token); } catch (OperationCanceledException) {}

            var lines = File.ReadAllLines(timelinePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            lines.Length.Should().Be(2); // FileCreated + WatcherStarted

            var fileCreatedEvent = JsonSerializer.Deserialize<OwsEvent>(lines[0]);
            fileCreatedEvent!.EventType.Should().Be(OwsEventType.FileCreated);
            fileCreatedEvent.Metadata.Should().ContainKey("source").WhoseValue.Should().Be("initial_baseline");
            fileCreatedEvent.Metadata.Should().ContainKey("usedForTrust").WhoseValue.Should().Be("false");
        }
        finally
        {
            EnsureCleanDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task IgnoredDirectories_DoNotProduceNoisyUnobservedChanges()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-ignored-{Guid.NewGuid():N}");
        EnsureCleanDirectory(projectRoot);

        try
        {
            var localOws = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            Directory.CreateDirectory(localOws);
            var snapshotPath = Path.Combine(localOws, OwsConstants.ObservedSnapshotFileName);
            var timelinePath = Path.Combine(localOws, OwsConstants.TimelineFileName);

            // Write initial snapshot with empty files
            var baseTime = DateTimeOffset.UtcNow.AddMinutes(-5);
            var snapshot = new ObservedSnapshot
            {
                ObservedAt = baseTime,
                Files = new Dictionary<string, ObservedFileState>()
            };
            File.WriteAllText(snapshotPath, JsonSerializer.Serialize(snapshot));

            var startedEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
            {
                EventType = OwsEventType.WatcherStarted,
                ProjectId = "test-project"
            }, OwsEventChain.GenesisPreviousEventHash);
            File.WriteAllText(timelinePath, JsonSerializer.Serialize(startedEvent) + Environment.NewLine);

            // Add files inside standard ignored directories (bin and obj)
            var binDir = Path.Combine(projectRoot, "bin");
            Directory.CreateDirectory(binDir);
            File.WriteAllText(Path.Combine(binDir, "output.dll"), "large binary content simulation here");

            var agent = new LocalTrackingAgent(NullLogger<LocalTrackingAgent>.Instance);
            var cts = new CancellationTokenSource();

            await agent.PrepareAsync(new TrackingAgentOptions
            {
                ProjectRootPath = projectRoot,
                DatabasePath = Path.Combine(localOws, "ows.db"),
                WasInterrupted = false
            }, cts.Token);

            cts.CancelAfter(200);
            try { await agent.StartAsync(cts.Token); } catch (OperationCanceledException) {}

            var lines = File.ReadAllLines(timelinePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            lines.Length.Should().Be(2);
            var eventTypes = lines.Select(l => JsonSerializer.Deserialize<OwsEvent>(l)!.EventType).ToList();
            eventTypes.Should().NotContain(OwsEventType.ObservationGapDetected);
            eventTypes.Should().NotContain(OwsEventType.UnobservedChangeDetected);
            eventTypes.Should().NotContain(OwsEventType.LargeUnobservedChangeDetected);
        }
        finally
        {
            EnsureCleanDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task SnapshotWrites_AreAtomic_AndRecoverSafelyFromCorruptSnapshot()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-corrupt-snapshot-{Guid.NewGuid():N}");
        EnsureCleanDirectory(projectRoot);

        try
        {
            var localOws = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            Directory.CreateDirectory(localOws);
            var snapshotPath = Path.Combine(localOws, OwsConstants.ObservedSnapshotFileName);
            var timelinePath = Path.Combine(localOws, OwsConstants.TimelineFileName);

            // Write corrupted snapshot (invalid JSON)
            File.WriteAllText(snapshotPath, "{invalid json content...");

            var agent = new LocalTrackingAgent(NullLogger<LocalTrackingAgent>.Instance);
            var cts = new CancellationTokenSource();

            await agent.PrepareAsync(new TrackingAgentOptions
            {
                ProjectRootPath = projectRoot,
                DatabasePath = Path.Combine(localOws, "ows.db"),
                WasInterrupted = false
            }, cts.Token);

            cts.CancelAfter(200);
            await agent.StartAsync(cts.Token);

            File.Exists(snapshotPath).Should().BeTrue();
            var content = File.ReadAllText(snapshotPath);
            var snapshot = JsonSerializer.Deserialize<ObservedSnapshot>(content);
            snapshot.Should().NotBeNull();
        }
        finally
        {
            EnsureCleanDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task LargeDeletion_TriggersLargeUnobservedChangeDetected()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-large-del-{Guid.NewGuid():N}");
        EnsureCleanDirectory(projectRoot);

        try
        {
            var localOws = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            Directory.CreateDirectory(localOws);
            var snapshotPath = Path.Combine(localOws, OwsConstants.ObservedSnapshotFileName);
            var timelinePath = Path.Combine(localOws, OwsConstants.TimelineFileName);

            // Write initial snapshot containing a large file
            var baseTime = DateTimeOffset.UtcNow.AddMinutes(-5);
            var snapshot = new ObservedSnapshot
            {
                ObservedAt = baseTime,
                Files = new Dictionary<string, ObservedFileState>
                {
                    { "work.txt", new ObservedFileState { RelativePath = "work.txt", FileHash = "h1", Size = 60000, LineCount = 500, ObservedAt = baseTime } }
                }
            };
            File.WriteAllText(snapshotPath, JsonSerializer.Serialize(snapshot));

            var startedEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
            {
                EventType = OwsEventType.WatcherStarted,
                ProjectId = "test-project"
            }, OwsEventChain.GenesisPreviousEventHash);
            File.WriteAllText(timelinePath, JsonSerializer.Serialize(startedEvent) + Environment.NewLine);

            // Delete the file in reality
            var realFile = Path.Combine(projectRoot, "work.txt");
            if (File.Exists(realFile)) File.Delete(realFile);

            var agent = new LocalTrackingAgent(NullLogger<LocalTrackingAgent>.Instance);
            var cts = new CancellationTokenSource();

            await agent.PrepareAsync(new TrackingAgentOptions
            {
                ProjectRootPath = projectRoot,
                DatabasePath = Path.Combine(localOws, "ows.db"),
                WasInterrupted = false
            }, cts.Token);

            cts.CancelAfter(200);
            try { await agent.StartAsync(cts.Token); } catch (OperationCanceledException) {}

            var lines = File.ReadAllLines(timelinePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            lines.Length.Should().Be(4);

            var largeEvent = JsonSerializer.Deserialize<OwsEvent>(lines[3]);
            largeEvent!.EventType.Should().Be(OwsEventType.LargeUnobservedChangeDetected);
            largeEvent.Metadata["changeKind"].Should().Be("Deleted");
            largeEvent.Metadata["bytesDelta"].Should().Be("-60000");
        }
        finally
        {
            EnsureCleanDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task ObservationGaps_DegradeToDegraded_NotInvalid()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-gap-{Guid.NewGuid():N}.owspkg");

        try
        {
            var hashService = new Sha256HashService();
            
            var startedEvent = new OwsEvent { EventType = OwsEventType.WatcherStarted, ProjectId = "sample" };
            var gapEvent = new OwsEvent
            {
                EventType = OwsEventType.ObservationGapDetected,
                ProjectId = "sample",
                Metadata = new Dictionary<string, string>
                {
                    { "gapStartedAt", DateTimeOffset.UtcNow.AddMinutes(-10).ToString("o") },
                    { "gapEndedAt", DateTimeOffset.UtcNow.ToString("o") },
                    { "gapDurationMs", "600000" },
                    { "previousState", "CleanStopped" }
                }
            };

            var timelineEvents = CreateChainedEvents(startedEvent, gapEvent);
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
            result.TrustStatus.Should().Be(TrustStatus.Degraded);
            result.Findings.Should().Contain(finding => finding.Code == "observation.gap");
        }
        finally
        {
            if (File.Exists(packagePath)) File.Delete(packagePath);
        }
    }

    [Fact]
    public async Task BrokenHashChain_RemainsInvalid()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-verify-broken-{Guid.NewGuid():N}.owspkg");

        try
        {
            var hashService = new Sha256HashService();
            
            var ev1 = OwsEventChain.CreateChainedEvent(new OwsEvent { EventType = OwsEventType.WatcherStarted, ProjectId = "sample" }, OwsEventChain.GenesisPreviousEventHash);
            var ev2 = OwsEventChain.CreateChainedEvent(new OwsEvent { EventType = OwsEventType.FileCreated, ProjectId = "sample" }, "fake_previous_hash_here");

            var timelineText = JsonSerializer.Serialize(ev1) + Environment.NewLine + JsonSerializer.Serialize(ev2);
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

            result.IsSuccess.Should().BeFalse();
            result.TrustStatus.Should().Be(TrustStatus.Invalid);
            result.Findings.Should().Contain(finding => finding.Code == "timeline.chain.broken");
        }
        finally
        {
            if (File.Exists(packagePath)) File.Delete(packagePath);
        }
    }

    private static string SerializeTimeline(params OwsEvent[] events) =>
        string.Join(Environment.NewLine, CreateChainedEvents(events).Select(owsEvent => JsonSerializer.Serialize(owsEvent)));

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

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
