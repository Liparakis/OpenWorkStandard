using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Ows.Core.Agent;
using Ows.Core.Init;
using Xunit;

namespace Ows.Core.Tests;

public sealed class OwsWatcherHardeningTests {
    [Fact]
    public async Task Watcher_ShouldRecoverFromStalePidLock() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-stale-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);

        try {
            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);

            var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            var watcherJsonPath = Path.Combine(localFolder, "watcher.json");

            // Write a stale watcher lock with a non-existent/dead PID
            var staleState = new WatcherProcessState {
                Pid = 999999, // Unlikely to exist, and process name won't match ows/dotnet/testhost anyway
                StartedAt = DateTimeOffset.UtcNow.AddHours(-1)
            };
            File.WriteAllText(watcherJsonPath, JsonSerializer.Serialize(staleState));

            // Verify it thinks the watcher is NOT running (since it's a stale lock)
            manager.IsWatcherRunning(projectRoot).Should().BeFalse();
            manager.DidWatcherCrash(projectRoot).Should().BeTrue();

            // Starting the watcher should clean up the stale PID lock and start successfully
            var watcherTask = Task.Run(() => manager.StartWatcherAsync(projectRoot, usePolling: true, debounceMs: 100));

            // Let watcher start
            for (int i = 0; i < 20; i++) {
                if (manager.IsWatcherRunning(projectRoot)) {
                    break;
                }
                await Task.Delay(100);
            }

            manager.IsWatcherRunning(projectRoot).Should().BeTrue();
            manager.DidWatcherCrash(projectRoot).Should().BeFalse();

            // Stop watcher
            await manager.StopWatcherAsync(projectRoot);
            await watcherTask;
        } finally {
            if (Directory.Exists(projectRoot)) {
                try { Directory.Delete(projectRoot, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Watcher_ShouldPreventDuplicateStarts() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-dup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);

        try {
            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);

            var watcherTask = Task.Run(() => manager.StartWatcherAsync(projectRoot, usePolling: true, debounceMs: 100));

            // Let watcher start
            for (int i = 0; i < 20; i++) {
                if (manager.IsWatcherRunning(projectRoot)) {
                    break;
                }
                await Task.Delay(100);
            }

            manager.IsWatcherRunning(projectRoot).Should().BeTrue();

            // Attempting to start duplicate watcher should throw InvalidOperationException
            Func<Task> act = () => manager.StartWatcherAsync(projectRoot, usePolling: true, debounceMs: 100);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Watcher is already running for this project.");

            // Stop watcher
            await manager.StopWatcherAsync(projectRoot);
            await watcherTask;
        } finally {
            if (Directory.Exists(projectRoot)) {
                try { Directory.Delete(projectRoot, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Watcher_ShouldStopCleanlyWhenAlreadyStopped() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-stop-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);

        try {
            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);

            manager.IsWatcherRunning(projectRoot).Should().BeFalse();

            // Stopping when already stopped should return cleanly without throwing
            Func<Task> act = () => manager.StopWatcherAsync(projectRoot);
            await act.Should().NotThrowAsync();
        } finally {
            if (Directory.Exists(projectRoot)) {
                try { Directory.Delete(projectRoot, recursive: true); } catch { }
            }
        }
    }
}
