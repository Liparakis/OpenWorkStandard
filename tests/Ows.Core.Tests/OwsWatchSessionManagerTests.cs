using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Ows.Core.Agent;
using Ows.Core.Init;
using Ows.Core.Notarization;
using Xunit;

namespace Ows.Core.Tests;

/// <summary>
/// Verifies the implementation of OwsWatchSessionManager.
/// </summary>
public sealed class OwsWatchSessionManagerTests {
    [Fact]
    public async Task Manager_ShouldManageProjectLifecycleCorrectly() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-mgr-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);

        try {
            var manager = new OwsWatchSessionManager();

            // 1. Initialized check
            manager.IsProjectInitialized(projectRoot).Should().BeFalse();
            manager.InitializeProject(projectRoot);
            manager.IsProjectInitialized(projectRoot).Should().BeTrue();

            // 2. Config reading/writing
            var config = manager.GetProjectConfig(projectRoot);
            config.Should().NotBeNull();
            config!.OwsVersion.Should().Be("0.1");

            config.VerifierUrl = "http://localhost:9999";
            config.InstitutionId = "inst-1";
            config.AssessmentId = "assess-1";
            config.StudentUserId = "std-1";
            manager.SaveProjectConfig(projectRoot, config);

            var loadedConfig = manager.GetProjectConfig(projectRoot);
            loadedConfig!.VerifierUrl.Should().Be("http://localhost:9999");
            loadedConfig.InstitutionId.Should().Be("inst-1");
            loadedConfig.AssessmentId.Should().Be("assess-1");
            loadedConfig.StudentUserId.Should().Be("std-1");

            // Clear verifier URL for local (in-memory) session execution
            config.VerifierUrl = null;
            manager.SaveProjectConfig(projectRoot, config);

            // 3. Local session start
            var sessionId = await manager.StartSessionAsync(projectRoot);
            sessionId.Should().NotBeNullOrWhiteSpace();

            manager.GetCurrentSessionId(projectRoot).Should().Be(sessionId);

            // 4. Local checkpoint addition
            // Create a dummy timeline file to prevent timeline check failure
            var localOwsFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            var timelinePath = Path.Combine(localOwsFolder, OwsConstants.TimelineFileName);
            File.WriteAllText(timelinePath, "");

            var checkpointHash = await manager.AddCheckpointAsync(projectRoot);
            checkpointHash.Should().NotBeNullOrWhiteSpace();
            manager.GetLastCheckpointAt(projectRoot).Should().NotBeNull();

            // 5. Watcher start and stop cycle
            manager.IsWatcherRunning(projectRoot).Should().BeFalse();

            // Run watcher start on a background task
            var watcherTask = Task.Run(() => manager.StartWatcherAsync(projectRoot, usePolling: true, debounceMs: 100));

            // Wait a moment to let watcher start and write watcher.json
            for (var i = 0; i < 20; i++) {
                if (manager.IsWatcherRunning(projectRoot)) {
                    break;
                }
                await Task.Delay(100);
            }

            manager.IsWatcherRunning(projectRoot).Should().BeTrue();

            // Stop the watcher
            await manager.StopWatcherAsync(projectRoot);
            manager.IsWatcherRunning(projectRoot).Should().BeFalse();

            // Wait for the background task to complete cleanly
            await watcherTask;
        } finally {
            if (Directory.Exists(projectRoot)) {
                try { Directory.Delete(projectRoot, recursive: true); } catch { }
            }
        }
    }
}
