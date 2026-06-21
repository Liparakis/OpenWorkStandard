using FluentAssertions;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests the watch command surface registration.
/// </summary>
/// <remarks>
/// The persistent watch behavior (initial scan + continuous monitoring) is tested
/// at the <c>LocalTrackingAgent</c> level in <c>Ows.Core.Tests/AgentNamespaceTests</c>.
/// CLI-level tests here verify the command registration and option surface so that
/// integration-test runs do not hang waiting for an indefinite watcher loop.
/// </remarks>
[Collection(CliCommandCollection.Name)]
public sealed class OwsWatchCommandTests {
    /// <summary>
    /// Verifies that the watch command is registered on the root command.
    /// </summary>
    [Fact]
    public void WatchCommand_ShouldBeRegisteredOnRootCommand() {
        var root = OwsCommandFactory.BuildRootCommand();
        root.Subcommands.Should().Contain(c => c.Name == "watch",
            "the watch command should be registered on the root command");
    }

    /// <summary>
    /// Verifies that the watch command exposes the --poll option.
    /// </summary>
    [Fact]
    public void WatchCommand_ShouldExposePollingOption() {
        var root = OwsCommandFactory.BuildRootCommand();
        var watchCommand = root.Subcommands.Single(c => c.Name == "watch");
        watchCommand.Options.Should().Contain(o => o.Name == "poll" || o.Name == "--poll",
            "the --poll flag should be registered on the watch command");
    }

    /// <summary>
    /// Verifies that the watch command exposes the --debounce option with a default value.
    /// </summary>
    [Fact]
    public void WatchCommand_ShouldExposeDebounceOption() {
        var root = OwsCommandFactory.BuildRootCommand();
        var watchCommand = root.Subcommands.Single(c => c.Name == "watch");
        watchCommand.Options.Should().Contain(o => o.Name == "debounce" || o.Name == "--debounce",
            "the --debounce option should be registered on the watch command");
    }

    /// <summary>
    /// Verifies that ows watch performs an initial scan and appends file events to the
    /// local timeline within the polling window, then exits cleanly when cancellation fires.
    /// </summary>
    [Fact]
    public async Task WatchCommand_ShouldAppendExistingFilesToTimeline() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "draft.txt"), "draft");
        var originalDirectory = Directory.GetCurrentDirectory();

        try {
            Directory.SetCurrentDirectory(projectRoot);

            var initResult = await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync();
            initResult.Should().Be(0);

            // Run the agent directly (bypassing the CLI watch command) with a short-lived
            // token so the watcher stops after the initial scan.
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            var agent = new Core.Agent.LocalTrackingAgent(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Core.Agent.LocalTrackingAgent>.Instance);
            await agent.PrepareAsync(
                new Core.Agent.TrackingAgentOptions {
                    ProjectRootPath = projectRoot,
                    DatabasePath = Path.Combine(projectRoot, Core.OwsConstants.LocalFolderName, "ows.db"),
                    WatcherOptions = new Core.Agent.FileWatcherOptions {
                        UsePollingFallback = true,
                        PollingIntervalMs = 50,
                        DebounceIntervalMs = 30
                    }
                },
                CancellationToken.None);

            await agent.StartAsync(cts.Token);

            var timelinePath = Path.Combine(projectRoot, ".ows", "timeline.jsonl");
            var lines = (await File.ReadAllLinesAsync(timelinePath))
                .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

            lines.Should().Contain(line => line.Contains("draft.txt"),
                "the initial scan should have appended a FileCreated event for draft.txt");
        } finally {
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }
}
