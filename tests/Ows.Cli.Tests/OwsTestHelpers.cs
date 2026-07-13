using Microsoft.Extensions.Logging.Abstractions;
using Ows.Core;
using Ows.Core.Agent;

namespace Ows.Cli.Tests;

/// <summary>
/// Shared helpers for CLI command tests.
/// </summary>
internal static class OwsTestHelpers {
    /// <summary>
    /// Runs a one-shot initial file scan for the given project root, equivalent to what
    /// The local Agent uses the same preparation path before becoming a persistent watcher.
    /// Uses the polling fallback with a very short interval so the scan completes in under
    /// 300 ms and the test does not hang waiting for a long-running watch loop.
    /// </summary>
    /// <param name="projectRoot">Absolute path to the project root.</param>
    public static async Task RunInitialScanAsync(string projectRoot) {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var agent = new LocalTrackingAgent(NullLogger<LocalTrackingAgent>.Instance);
        await agent.PrepareAsync(
            new TrackingAgentOptions {
                ProjectRootPath = projectRoot,
                DatabasePath = Path.Combine(projectRoot, OwsConstants.LocalFolderName, "ows.db"),
                WatcherOptions = new FileWatcherOptions {
                    UsePollingFallback = true,
                    PollingIntervalMs = 50,
                    DebounceIntervalMs = 30
                }
            },
            CancellationToken.None);
        await agent.StartAsync(cts.Token);
    }
}
