using System.Text.Json;
using FluentAssertions;
using Ows.Core.Agent;
using Ows.Core.Events;

namespace Ows.Core.Tests;

/// <summary>
/// Verifies OWS v0.1 event emission, state transitions, secret scrubbing, and validation rules.
/// </summary>
public sealed class LifecycleEmitterTests
{
    private static void EnsureCleanProject(string projectRoot)
    {
        if (Directory.Exists(projectRoot))
        {
            Directory.Delete(projectRoot, recursive: true);
        }

        Directory.CreateDirectory(projectRoot);
    }

    [Fact]
    public async Task StartWatcher_ShouldEmitProjectOpened_And_DuplicateStart_ShouldThrowAndNotEmitDuplicate()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-lifecycle-start-{Guid.NewGuid():N}");
        EnsureCleanProject(projectRoot);

        try
        {
            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);

            // Verify watcher is not running initially
            manager.IsWatcherRunning(projectRoot).Should().BeFalse();

            // Start the watcher
            var watcherTask = Task.Run(() => manager.StartWatcherAsync(projectRoot, usePolling: true, debounceMs: 100));

            // Wait a moment for watcher to start
            for (int i = 0; i < 20; i++)
            {
                if (manager.IsWatcherRunning(projectRoot))
                {
                    break;
                }

                await Task.Delay(100);
            }

            manager.IsWatcherRunning(projectRoot).Should().BeTrue();

            // Attempt duplicate start (should throw)
            Func<Task> duplicateStart = async () =>
                await manager.StartWatcherAsync(projectRoot, usePolling: true, debounceMs: 100);
            await duplicateStart.Should().ThrowAsync<InvalidOperationException>();

            // Stop watcher
            await manager.StopWatcherAsync(projectRoot);
            await watcherTask;

            // Read timeline
            var localOws = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            var timelinePath = Path.Combine(localOws, OwsConstants.TimelineFileName);
            File.Exists(timelinePath).Should().BeTrue();

            var lines = await File.ReadAllLinesAsync(timelinePath);
            var openedEventsCount = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var owsEvent = JsonSerializer.Deserialize<OwsEvent>(line);
                if (owsEvent?.EventType == OwsEventType.ProjectOpened)
                {
                    openedEventsCount++;
                }
            }

            openedEventsCount.Should()
                .Be(1, "there should be exactly one ProjectOpened event recorded and no duplicates.");
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                try
                {
                    Directory.Delete(projectRoot, recursive: true);
                }
                catch
                {
                    /*ignored*/
                }
            }
        }
    }

    [Fact]
    public async Task StopWatcher_WhenAlreadyStopped_ShouldNotEmitProjectClosed()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-lifecycle-stop-stopped-{Guid.NewGuid():N}");
        EnsureCleanProject(projectRoot);

        try
        {
            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);

            // Call stop while not running
            await manager.StopWatcherAsync(projectRoot);

            var localOws = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            var timelinePath = Path.Combine(localOws, OwsConstants.TimelineFileName);

            // If timeline exists, check that it contains no ProjectClosed events
            if (File.Exists(timelinePath))
            {
                var lines = await File.ReadAllLinesAsync(timelinePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var owsEvent = JsonSerializer.Deserialize<OwsEvent>(line);
                    owsEvent?.EventType.Should().NotBe(OwsEventType.ProjectClosed);
                }
            }
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                try
                {
                    Directory.Delete(projectRoot, recursive: true);
                }
                catch
                {
                    /*ignored*/
                }
            }
        }
    }

    [Fact]
    public async Task StopWatcher_WithStalePid_ShouldNotEmitProjectClosed()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-lifecycle-stale-pid-{Guid.NewGuid():N}");
        EnsureCleanProject(projectRoot);

        try
        {
            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);

            // Write fake watcher.json with stale/dead PID
            var localOws = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            var watcherJsonPath = Path.Combine(localOws, "watcher.json");
            var state = new WatcherProcessState
            {
                Pid = 999999, // Dead/stale PID
                StartedAt = DateTimeOffset.UtcNow
            };
            await File.WriteAllTextAsync(watcherJsonPath, JsonSerializer.Serialize(state));

            manager.IsWatcherRunning(projectRoot).Should().BeFalse("watcher process is dead.");

            // Stop watcher (performs stale PID cleanup)
            await manager.StopWatcherAsync(projectRoot);

            // watcher.json should be cleaned up
            File.Exists(watcherJsonPath).Should().BeFalse();

            // Timeline (if exists) should not contain ProjectClosed
            var timelinePath = Path.Combine(localOws, OwsConstants.TimelineFileName);
            if (File.Exists(timelinePath))
            {
                var lines = await File.ReadAllLinesAsync(timelinePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var owsEvent = JsonSerializer.Deserialize<OwsEvent>(line);
                    owsEvent?.EventType.Should().NotBe(OwsEventType.ProjectClosed);
                }
            }
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                try
                {
                    Directory.Delete(projectRoot, recursive: true);
                }
                catch
                {
                    /*ignored*/
                }
            }
        }
    }

    [Fact]
    public async Task PackageFailure_ShouldNotEmitPackageCreated()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-lifecycle-pkg-fail-{Guid.NewGuid():N}");
        EnsureCleanProject(projectRoot);

        try
        {
            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);

            // Delete timeline to force a package failure inside OwsPackageBuilder (reads timeline)
            var localOws = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            var timelinePath = Path.Combine(localOws, OwsConstants.TimelineFileName);
            if (File.Exists(timelinePath))
            {
                File.Delete(timelinePath);
            }

            Func<Task> packageAction = async () => await manager.PackageProjectAsync(projectRoot);
            await packageAction.Should().ThrowAsync<Exception>();

            // Confirm no timeline was recreated containing PackageCreated
            if (File.Exists(timelinePath))
            {
                var lines = await File.ReadAllLinesAsync(timelinePath);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var owsEvent = JsonSerializer.Deserialize<OwsEvent>(line);
                    owsEvent?.EventType.Should().NotBe(OwsEventType.PackageCreated);
                }
            }
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                try
                {
                    Directory.Delete(projectRoot, recursive: true);
                }
                catch
                {
                    /*ignored*/
                }
            }
        }
    }

    [Fact]
    public async Task GenericEventEmission_ShouldEnforceAllowlist()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-lifecycle-allowlist-{Guid.NewGuid():N}");
        EnsureCleanProject(projectRoot);

        try
        {
            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);

            var allowedTypes = new[]
            {
                OwsEventType.BuildStarted,
                OwsEventType.BuildSucceeded,
                OwsEventType.BuildFailed,
                OwsEventType.TestExecuted,
                OwsEventType.ProgramExecuted
            };

            foreach (var type in allowedTypes)
            {
                Func<Task> allowedCall = async () =>
                    await manager.EmitGenericEventAsync(projectRoot, type, "cli", "test-label", 0, 100);
                await allowedCall.Should().NotThrowAsync();
            }

            var disallowedTypes = new[]
            {
                OwsEventType.FileCreated,
                OwsEventType.FileModified,
                OwsEventType.FileDeleted,
                OwsEventType.ProjectOpened,
                OwsEventType.ProjectClosed,
                OwsEventType.LargeInsert,
                OwsEventType.PackageCreated
            };

            foreach (var type in disallowedTypes)
            {
                Func<Task> disallowedCall = async () =>
                    await manager.EmitGenericEventAsync(projectRoot, type, "cli", "test-label");
                await disallowedCall.Should().ThrowAsync<ArgumentException>();
            }
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                try
                {
                    Directory.Delete(projectRoot, recursive: true);
                }
                catch
                {
                    /*ignored*/
                }
            }
        }
    }

    [Theory]
    [InlineData("password=abcdef123", "password=[REDACTED]")]
    [InlineData("my-token: std_123456", "my-token: [REDACTED]")]
    [InlineData("bearer 9876543210", "bearer [REDACTED]")]
    [InlineData("secretKey: abcdefghi", "secretKey: [REDACTED]")]
    [InlineData("normal label command", "normal label command")]
    [InlineData("api_key = key_value123", "api_key = [REDACTED]")]
    public void ScrubSecrets_ShouldRedactSensitiveInformation(string input, string expected)
    {
        var result = OwsWatchSessionManager.ScrubSecrets(input);
        result.Should().Be(expected);
    }
}