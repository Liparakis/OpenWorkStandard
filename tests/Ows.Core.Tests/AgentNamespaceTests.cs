using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ows.Core.Agent;
using Ows.Core.Agent.Scanning;
using Ows.Core.Events;

namespace Ows.Core.Tests;

/// <summary>
/// Tests agent types after consolidation into Ows.Core.
/// </summary>
public sealed class AgentNamespaceTests {
    /// <summary>
    /// Verifies the tracking agent status enum is exposed from Ows.Core.
    /// </summary>
    [Fact]
    public void TrackingAgentStatus_ShouldExposeIdleState() {
        TrackingAgentStatus.Idle.Should().Be(TrackingAgentStatus.Idle);
    }

    /// <summary>
    /// Verifies that starting the agent appends file events for existing project files and stops cleanly when cancelled.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task StartAsync_ShouldAppendExistingFilesToTimeline() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var trackedFile = Path.Combine(projectRoot, "notes.txt");
        File.WriteAllText(trackedFile, "hello");

        try {
            var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            Directory.CreateDirectory(localFolder);
            var timelinePath = Path.Combine(localFolder, OwsConstants.TimelineFileName);
            File.WriteAllText(timelinePath, string.Empty);

            // Use a very short polling interval so the test finishes quickly.
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            var agent = new LocalTrackingAgent(new NullLogger<LocalTrackingAgent>());
            await agent.PrepareAsync(
                new TrackingAgentOptions {
                    ProjectRootPath = projectRoot,
                    DatabasePath = Path.Combine(localFolder, "ows.db"),
                    WatcherOptions = new FileWatcherOptions {
                        UsePollingFallback = true,
                        PollingIntervalMs = 50,
                        DebounceIntervalMs = 30
                    }
                },
                CancellationToken.None);

            // StartAsync blocks until the token is cancelled — cancel after 200 ms.
            var result = await agent.StartAsync(cts.Token);
            var lines = File.ReadAllLines(timelinePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            var events = lines.Select(line => JsonSerializer.Deserialize<OwsEvent>(line)!).ToArray();

            result.Succeeded.Should().BeTrue();
            result.Status.Should().Be(TrackingAgentStatus.Stopped);
            events.Select(owsEvent => owsEvent.EventType).Should().ContainInOrder(
                OwsEventType.FileCreated,
                OwsEventType.WatcherStarted,
                OwsEventType.SnapshotUpdated);

            var trackedEvent = events[0];
            trackedEvent.RelativePath.Should().Be("notes.txt");
            trackedEvent.PreviousEventHash.Should().Be(OwsEventChain.GenesisPreviousEventHash);
            trackedEvent.EventHash.Should().NotBeNullOrWhiteSpace();

            var startedEvent = events[1];
            startedEvent.PreviousEventHash.Should().Be(trackedEvent.EventHash);
            events[2].PreviousEventHash.Should().Be(startedEvent.EventHash);
        } finally {
            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that the continuous watch loop appends a FileModified event when a tracked file is written.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task WatchAsync_ShouldAppendModifiedFileEvent() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var trackedFile = Path.Combine(projectRoot, "work.txt");
        File.WriteAllText(trackedFile, "initial content");

        try {
            var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            Directory.CreateDirectory(localFolder);
            var timelinePath = Path.Combine(localFolder, OwsConstants.TimelineFileName);
            File.WriteAllText(timelinePath, string.Empty);

            using var cts = new CancellationTokenSource();

            var agent = new LocalTrackingAgent(new NullLogger<LocalTrackingAgent>());
            await agent.PrepareAsync(
                new TrackingAgentOptions {
                    ProjectRootPath = projectRoot,
                    DatabasePath = Path.Combine(localFolder, "ows.db"),
                    WatcherOptions = new FileWatcherOptions {
                        UsePollingFallback = true,
                        PollingIntervalMs = 50,
                        DebounceIntervalMs = 30
                    }
                },
                CancellationToken.None);

            // Run the agent in the background.
            var agentTask = agent.StartAsync(cts.Token);

            // Give the initial scan time to complete, then modify a file.
            await Task.Delay(100);
            File.WriteAllText(trackedFile, "updated content");

            // Wait for the polling loop to detect the change and the debounce to expire.
            await Task.Delay(300);
            await cts.CancelAsync();

            await agentTask;

            var lines = File.ReadAllLines(timelinePath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            // First line is the initial scan FileCreated event.
            lines.Length.Should().BeGreaterThan(1, "the watcher should have appended a FileModified event");
            var modifiedLine = lines.Skip(1).FirstOrDefault(l => l.Contains(nameof(OwsEventType.FileModified)));
            modifiedLine.Should().NotBeNull("a FileModified event should have been recorded for work.txt");
            modifiedLine!.Should().Contain("work.txt");

            // Verify the chain is unbroken.
            var allEvents = lines.Select(l => JsonSerializer.Deserialize<OwsEvent>(l)!).ToList();
            for (var i = 1; i < allEvents.Count; i++) {
                allEvents[i].PreviousEventHash.Should().Be(allEvents[i - 1].EventHash,
                    "each event must chain to the previous one");
            }
        } finally {
            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that the continuous watch loop appends a FileDeleted event when a tracked file is removed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task WatchAsync_ShouldAppendDeletedFileEvent() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var trackedFile = Path.Combine(projectRoot, "draft.txt");
        File.WriteAllText(trackedFile, "some content");

        try {
            var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            Directory.CreateDirectory(localFolder);
            var timelinePath = Path.Combine(localFolder, OwsConstants.TimelineFileName);
            File.WriteAllText(timelinePath, string.Empty);

            using var cts = new CancellationTokenSource();

            var agent = new LocalTrackingAgent(new NullLogger<LocalTrackingAgent>());
            await agent.PrepareAsync(
                new TrackingAgentOptions {
                    ProjectRootPath = projectRoot,
                    DatabasePath = Path.Combine(localFolder, "ows.db"),
                    WatcherOptions = new FileWatcherOptions {
                        UsePollingFallback = true,
                        PollingIntervalMs = 50,
                        DebounceIntervalMs = 30
                    }
                },
                CancellationToken.None);

            var agentTask = agent.StartAsync(cts.Token);

            // Give the initial scan time to complete, then delete the file.
            await Task.Delay(100);
            File.Delete(trackedFile);

            // Wait for the polling loop to detect the deletion and the debounce to expire.
            await Task.Delay(300);
            await cts.CancelAsync();

            await agentTask;

            var lines = File.ReadAllLines(timelinePath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            lines.Length.Should().BeGreaterThan(1, "the watcher should have appended a FileDeleted event");
            var deletedLine = lines.Skip(1).FirstOrDefault(l => l.Contains(nameof(OwsEventType.FileDeleted)));
            deletedLine.Should().NotBeNull("a FileDeleted event should have been recorded for draft.txt");
            deletedLine!.Should().Contain("draft.txt");
        } finally {
            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }
}
