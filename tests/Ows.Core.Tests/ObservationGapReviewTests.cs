using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ows.Core.Agent;
using Ows.Core.Events;
using Ows.Core.Init;
using Ows.Core.Notarization;
using Ows.Core.Packaging;
using Ows.Core.Reporting;
using Ows.Core.Verification;

namespace Ows.Core.Tests;

public sealed class ObservationGapReviewTests {
    private static readonly string[] BannedAccusatoryPhrases =
    [
        "cheating detected",
        "cheater",
        "misconduct detected",
        "AI-generated",
        "AI detection",
        "plagiarism detected",
        "plagiarized",
        "guilty",
        "fraud",
        "suspicious"
    ];

    [Fact]
    public async Task CleanStop_LargeUnobservedChange_ReportStaysReviewOriented() {
        var projectRoot = CreateProjectRoot("ows-gap-review-clean");

        try {
            var draftPath = Path.Combine(projectRoot, "draft.txt");
            await File.WriteAllTextAsync(draftPath, "draft");

            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);
            await manager.StartSessionAsync(projectRoot);

            await RunWatcherScanAsync(projectRoot);
            await AppendLifecycleEventAsync(projectRoot, OwsEventType.WatcherStopped, new Dictionary<string, string> {
                ["reason"] = "user_requested"
            });

            await File.WriteAllTextAsync(draftPath, new string('A', 60000));

            await RunWatcherScanAsync(projectRoot);

            var timelineEvents = ReadTimeline(projectRoot);
            timelineEvents.Should().Contain(e => e.EventType == OwsEventType.ObservationGapDetected);
            timelineEvents.Should().Contain(e => e.EventType == OwsEventType.LargeUnobservedChangeDetected);
            timelineEvents.Any(e =>
                    e.EventType == OwsEventType.WatcherStarted &&
                    e.Metadata.TryGetValue("reason", out var reason) &&
                    reason == "user_restart")
                .Should()
                .BeTrue();

            var outcome = await PackageVerifyAndReportAsync(projectRoot, manager);

            outcome.VerificationResult.TrustStatus.Should().Be(TrustStatus.Degraded);
            AssertTrustIsNotInvalid(outcome.VerificationResult);
            AssertFindingPresent(outcome.VerificationResult, "observation.gap");
            AssertFindingPresent(outcome.VerificationResult, "observation.large_unobserved_change");
            AssertContainsEvidenceDisclaimer(outcome.ReportText);
            outcome.ReportText.Should().Contain("OWS cannot determine the cause.");
            outcome.ReportText.Should().Contain("This is not proof of misconduct.");
            outcome.ReportText.Should().Contain("Reviewers should ask the student to explain this interval.");
            AssertNonAccusatoryReport(outcome.ReportText);
        } finally {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task InterruptedRestart_SmallUnobservedChange_DegradesWithoutLargeChangeAccusation() {
        var projectRoot = CreateProjectRoot("ows-gap-review-interrupted");

        try {
            var draftPath = Path.Combine(projectRoot, "draft.txt");
            await File.WriteAllTextAsync(draftPath, "line 1");

            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);
            await manager.StartSessionAsync(projectRoot);

            await RunWatcherScanAsync(projectRoot);

            await File.WriteAllTextAsync(draftPath, "line 1" + Environment.NewLine + "line 2");

            await RunWatcherScanAsync(projectRoot, wasInterrupted: true, interruptedState: new WatcherProcessState {
                Pid = 424242,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            });

            var timelineEvents = ReadTimeline(projectRoot);
            timelineEvents.Should().Contain(e => e.EventType == OwsEventType.WatcherInterrupted);
            timelineEvents.Should().Contain(e => e.EventType == OwsEventType.WatcherRecovered);
            timelineEvents.Should().Contain(e => e.EventType == OwsEventType.ObservationGapDetected);
            timelineEvents.Should().Contain(e => e.EventType == OwsEventType.UnobservedChangeDetected);
            timelineEvents.Should().NotContain(e => e.EventType == OwsEventType.LargeUnobservedChangeDetected);

            var outcome = await PackageVerifyAndReportAsync(projectRoot, manager);

            outcome.VerificationResult.TrustStatus.Should().Be(TrustStatus.Degraded);
            AssertTrustIsNotInvalid(outcome.VerificationResult);
            AssertFindingPresent(outcome.VerificationResult, "observation.gap");
            AssertFindingPresent(outcome.VerificationResult, "observation.unobserved_change");
            AssertFindingAbsent(outcome.VerificationResult, "observation.large_unobserved_change");
            AssertContainsEvidenceDisclaimer(outcome.ReportText);
            outcome.ReportText.Should().Contain("During that interval, file changes appeared.");
            outcome.ReportText.Should().Contain("OWS can verify the current package hashes");
            outcome.ReportText.Should().Contain("cannot verify the unobserved edit process");
            AssertNonAccusatoryReport(outcome.ReportText);
        } finally {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task CleanRestartWithoutChanges_DoesNotInventObservationGapFindings() {
        var projectRoot = CreateProjectRoot("ows-gap-review-nochange");

        try {
            var draftPath = Path.Combine(projectRoot, "draft.txt");
            await File.WriteAllTextAsync(draftPath, "draft");

            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);
            await manager.StartSessionAsync(projectRoot);

            await RunWatcherScanAsync(projectRoot);
            await AppendLifecycleEventAsync(projectRoot, OwsEventType.WatcherStopped, new Dictionary<string, string> {
                ["reason"] = "user_requested"
            });

            await RunWatcherScanAsync(projectRoot);

            var outcome = await PackageVerifyAndReportAsync(projectRoot, manager);

            outcome.VerificationResult.TrustStatus.Should().Be(TrustStatus.Verified);
            AssertTrustIsNotInvalid(outcome.VerificationResult);
            AssertFindingAbsent(outcome.VerificationResult, "observation.gap");
            AssertFindingAbsent(outcome.VerificationResult, "observation.large_unobserved_change");
            AssertContainsEvidenceDisclaimer(outcome.ReportText);
            AssertNonAccusatoryReport(outcome.ReportText);
        } finally {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task MissingSnapshotAfterPriorTracking_DegradesContinuityInsteadOfSilentlyTrustingRestart() {
        var projectRoot = CreateProjectRoot("ows-gap-review-missing-snapshot");

        try {
            var draftPath = Path.Combine(projectRoot, "draft.txt");
            await File.WriteAllTextAsync(draftPath, "draft");

            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);
            await manager.StartSessionAsync(projectRoot);

            await RunWatcherScanAsync(projectRoot);

            var snapshotPath = Path.Combine(projectRoot, OwsConstants.LocalFolderName, OwsConstants.ObservedSnapshotFileName);
            File.Delete(snapshotPath);

            await RunWatcherScanAsync(projectRoot);

            var outcome = await PackageVerifyAndReportAsync(projectRoot, manager);

            outcome.VerificationResult.TrustStatus.Should().Be(TrustStatus.Degraded);
            AssertTrustIsNotInvalid(outcome.VerificationResult);
            AssertFindingPresent(outcome.VerificationResult, "observation.gap");
            AssertContainsEvidenceDisclaimer(outcome.ReportText);
            AssertNonAccusatoryReport(outcome.ReportText);
        } finally {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task CorruptSnapshotAfterPriorTracking_DegradesContinuityInsteadOfSilentlyTrustingRestart() {
        var projectRoot = CreateProjectRoot("ows-gap-review-corrupt-snapshot");

        try {
            var draftPath = Path.Combine(projectRoot, "draft.txt");
            await File.WriteAllTextAsync(draftPath, "draft");

            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);
            await manager.StartSessionAsync(projectRoot);

            await RunWatcherScanAsync(projectRoot);

            var snapshotPath = Path.Combine(projectRoot, OwsConstants.LocalFolderName, OwsConstants.ObservedSnapshotFileName);
            await File.WriteAllTextAsync(snapshotPath, "{not json");

            await RunWatcherScanAsync(projectRoot);

            var outcome = await PackageVerifyAndReportAsync(projectRoot, manager);

            outcome.VerificationResult.TrustStatus.Should().Be(TrustStatus.Degraded);
            AssertTrustIsNotInvalid(outcome.VerificationResult);
            AssertFindingPresent(outcome.VerificationResult, "observation.gap");
            AssertContainsEvidenceDisclaimer(outcome.ReportText);
            AssertNonAccusatoryReport(outcome.ReportText);
        } finally {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task LargeUnobservedChangeReport_DistinguishesCurrentHashesFromObservedEditHistory() {
        var projectRoot = CreateProjectRoot("ows-gap-review-wording");

        try {
            var draftPath = Path.Combine(projectRoot, "draft.txt");
            await File.WriteAllTextAsync(draftPath, "draft");

            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);
            await manager.StartSessionAsync(projectRoot);

            await RunWatcherScanAsync(projectRoot);
            await AppendLifecycleEventAsync(projectRoot, OwsEventType.WatcherStopped, new Dictionary<string, string> {
                ["reason"] = "user_requested"
            });

            await File.WriteAllTextAsync(draftPath, new string('B', 65000));
            await RunWatcherScanAsync(projectRoot);

            var outcome = await PackageVerifyAndReportAsync(projectRoot, manager);

            outcome.ReportText.Should().Contain("OWS can verify the current package hashes");
            outcome.ReportText.Should().Contain("cannot verify the unobserved edit process");
            AssertNonAccusatoryReport(outcome.ReportText);
        } finally {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task ValidButTamperedSnapshot_DegradesContinuity_NotInvalid() {
        var projectRoot = CreateProjectRoot("ows-gap-review-tampered-snapshot");

        try {
            var draftPath = Path.Combine(projectRoot, "draft.txt");
            await File.WriteAllTextAsync(draftPath, "draft");

            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);
            await manager.StartSessionAsync(projectRoot);

            await RunWatcherScanAsync(projectRoot);
            await AppendLifecycleEventAsync(projectRoot, OwsEventType.WatcherStopped, new Dictionary<string, string> {
                ["reason"] = "user_requested"
            });

            var snapshotPath = Path.Combine(projectRoot, OwsConstants.LocalFolderName, OwsConstants.ObservedSnapshotFileName);
            var snapshot = JsonSerializer.Deserialize<ObservedSnapshot>(await File.ReadAllTextAsync(snapshotPath));
            snapshot!.Files["draft.txt"] = new ObservedFileState {
                RelativePath = "draft.txt",
                FileHash = "fake-hash",
                Size = 9999,
                LineCount = 99,
                LastWriteTime = DateTimeOffset.UtcNow.AddHours(-1),
                ObservedAt = DateTimeOffset.UtcNow.AddHours(-1)
            };
            await File.WriteAllTextAsync(snapshotPath, JsonSerializer.Serialize(snapshot));

            await RunWatcherScanAsync(projectRoot);

            var outcome = await PackageVerifyAndReportAsync(projectRoot, manager);

            outcome.VerificationResult.TrustStatus.Should().Be(TrustStatus.Degraded);
            AssertTrustIsNotInvalid(outcome.VerificationResult);
            AssertFindingPresent(outcome.VerificationResult, "observation.snapshot_mismatch");
            outcome.ReportText.Should().Contain("OWS could not verify that the local recovery snapshot matched the last committed snapshot state.");
            outcome.ReportText.Should().Contain("This is not proof of misconduct.");
            AssertNonAccusatoryReport(outcome.ReportText);
        } finally {
            DeleteDirectory(projectRoot);
        }
    }

    private static void AssertContainsEvidenceDisclaimer(string reportText) {
        reportText.Should().Contain("Event presence is evidence of recorded activity. Event absence is not proof of misconduct.");
    }

    private static void AssertNonAccusatoryReport(string reportText) {
        foreach (var bannedPhrase in BannedAccusatoryPhrases) {
            reportText.Should().NotContain(bannedPhrase, because: "review reports must not imply that Open Work Standard judged the student");
        }
    }

    private static void AssertTrustIsNotInvalid(VerificationResult verificationResult) {
        verificationResult.IsSuccess.Should().BeTrue();
        verificationResult.TrustStatus.Should().NotBe(TrustStatus.Invalid);
    }

    private static void AssertFindingPresent(VerificationResult verificationResult, string findingCode) {
        verificationResult.Findings.Should().Contain(finding => finding.Code == findingCode);
    }

    private static void AssertFindingAbsent(VerificationResult verificationResult, string findingCode) {
        verificationResult.Findings.Should().NotContain(finding => finding.Code == findingCode);
    }

    private static async Task RunWatcherScanAsync(
        string projectRoot,
        bool wasInterrupted = false,
        WatcherProcessState? interruptedState = null) {
        var agent = new LocalTrackingAgent(NullLogger<LocalTrackingAgent>.Instance);
        var localOws = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        using var cts = new CancellationTokenSource();

        await agent.PrepareAsync(new TrackingAgentOptions {
            ProjectRootPath = projectRoot,
            DatabasePath = Path.Combine(localOws, "ows.db"),
            WasInterrupted = wasInterrupted,
            InterruptedState = interruptedState
        }, cts.Token);

        cts.CancelAfter(200);
        try {
            await agent.StartAsync(cts.Token);
        } catch (OperationCanceledException) {
        }
    }

    private static async Task AppendLifecycleEventAsync(
        string projectRoot,
        OwsEventType eventType,
        IReadOnlyDictionary<string, string> metadata) {
        var timelinePath = Path.Combine(projectRoot, OwsConstants.LocalFolderName, OwsConstants.TimelineFileName);
        var previousEventHash = File.Exists(timelinePath)
            ? OwsEventChain.ReadLastEventHash(timelinePath)
            : OwsEventChain.GenesisPreviousEventHash;

        var owsEvent = OwsEventChain.CreateChainedEvent(new OwsEvent {
            EventType = eventType,
            ProjectId = Path.GetFileName(projectRoot),
            ToolName = "ows watch",
            Metadata = new Dictionary<string, string>(metadata)
        }, previousEventHash);

        await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(owsEvent)}{Environment.NewLine}");
    }

    private static IReadOnlyList<OwsEvent> ReadTimeline(string projectRoot) {
        var timelinePath = Path.Combine(projectRoot, OwsConstants.LocalFolderName, OwsConstants.TimelineFileName);
        return File.ReadAllLines(timelinePath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<OwsEvent>(line)!)
            .ToArray();
    }

    private static async Task<(VerificationResult VerificationResult, string ReportText)> PackageVerifyAndReportAsync(
        string projectRoot,
        OwsWatchSessionManager manager) {
        await manager.AddCheckpointAsync(projectRoot);

        var builder = new OwsPackageBuilder();
        var packagePath = Path.Combine(projectRoot, "submission.owspkg");
        var packageResult = await builder.CreatePackageAsync(new PackageCreationRequest {
            ProjectRootPath = projectRoot,
            OutputPackagePath = packagePath
        }, CancellationToken.None);

        packageResult.Created.Should().BeTrue();

        var verificationResult = await new OwsPackageVerifier().VerifyAsync(new PackageVerificationRequest {
            PackagePath = packagePath
        }, CancellationToken.None);

        var reportText = (await new OwsReportGenerator().GenerateAsync(new ReportRequest {
            Format = ReportFormat.Text,
            VerificationResult = verificationResult
        }, CancellationToken.None)).Content;

        return (verificationResult, reportText);
    }

    private static string CreateProjectRoot(string prefix) {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        return projectRoot;
    }

    private static void DeleteDirectory(string path) {
        if (Directory.Exists(path)) {
            try {
                Directory.Delete(path, recursive: true);
            } catch {
            }
        }
    }
}
