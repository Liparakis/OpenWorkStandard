using System.Text.Json;
using FluentAssertions;
using Ows.Core.Agent;

namespace Ows.Core.Tests;

/// <summary>
///     Represents the <see cref="OwsWatcherHardeningTests" /> type.
/// </summary>
public sealed class OwsWatcherHardeningTests {
    /// <summary>
    ///     Verifies that the watcher process correctly detects and recovers from a stale PID lock file.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task Watcher_ShouldRecoverFromStalePidLock() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-stale-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);

        try {
            var manager = new OwsProjectAgent();
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
            var watcherTask = Task.Run(() => manager.StartWatcherAsync(projectRoot, true, 100));

            // Let watcher start
            for (var i = 0; i < 20; i++) {
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
                try {
                    Directory.Delete(projectRoot, true);
                } catch {
                }
            }
        }
    }

    /// <summary>
    ///     Verifies that starting a duplicate watcher on the same project root is prevented.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task Watcher_ShouldPreventDuplicateStarts() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-dup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);

        try {
            var manager = new OwsProjectAgent();
            manager.InitializeProject(projectRoot);

            var watcherTask = Task.Run(() => manager.StartWatcherAsync(projectRoot, true, 100));

            // Let watcher start
            for (var i = 0; i < 20; i++) {
                if (manager.IsWatcherRunning(projectRoot)) {
                    break;
                }

                await Task.Delay(100);
            }

            manager.IsWatcherRunning(projectRoot).Should().BeTrue();

            // Attempting to start duplicate watcher should throw InvalidOperationException
            var act = () => manager.StartWatcherAsync(projectRoot, true, 100);
            await act.Should().ThrowAsync<InvalidOperationException>()
                     .WithMessage("Watcher is already running for this project.");

            // Stop watcher
            await manager.StopWatcherAsync(projectRoot);
            await watcherTask;
        } finally {
            if (Directory.Exists(projectRoot)) {
                try {
                    Directory.Delete(projectRoot, true);
                } catch {
                }
            }
        }
    }

    /// <summary>
    ///     Verifies that stopping a watcher that is not running completes cleanly.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task Watcher_ShouldStopCleanlyWhenAlreadyStopped() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-stop-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);

        try {
            var manager = new OwsProjectAgent();
            manager.InitializeProject(projectRoot);

            manager.IsWatcherRunning(projectRoot).Should().BeFalse();

            // Stopping when already stopped should return cleanly without throwing
            var act = () => manager.StopWatcherAsync(projectRoot);
            await act.Should().NotThrowAsync();
        } finally {
            if (Directory.Exists(projectRoot)) {
                try {
                    Directory.Delete(projectRoot, true);
                } catch {
                }
            }
        }
    }
}
