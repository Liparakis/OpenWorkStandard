using System.CommandLine;
using Ows.Core.Agent;
using Ows.Core.Events;

namespace Ows.Cli.Commands;

/// <summary>
/// Provides construction for the event command group.
/// </summary>
public static class EventCommandBuilder {
    /// <summary>
    /// Builds the event command group to record explicit OWS timeline events.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build() {
        var command = new Command("event", "Record explicit OWS timeline events.");
        command.Hidden = true;

        var hostOption = new Option<string?>("--host") {
            Description = "Host application or environment identifier (for example, cli or another local host)."
        };
        var labelOption = new Option<string?>("--label") {
            Description = "Safe label or command name (secrets will be redacted)."
        };

        var buildStarted = new Command("build-started", "Record a build started event.");
        buildStarted.Options.Add(hostOption);
        buildStarted.Options.Add(labelOption);
        buildStarted.SetAction(async parseResult =>
            await HandleEventAction(parseResult, OwsEventType.BuildStarted, hostOption, labelOption));

        var buildSucceeded = new Command("build-succeeded", "Record a build succeeded event.");
        buildSucceeded.Options.Add(hostOption);
        buildSucceeded.Options.Add(labelOption);
        var succeededDuration = new Option<long?>("--duration") { Description = "Duration in milliseconds." };
        buildSucceeded.Options.Add(succeededDuration);
        buildSucceeded.SetAction(async parseResult => await HandleEventAction(parseResult, OwsEventType.BuildSucceeded,
            hostOption, labelOption, durationOption: succeededDuration));

        var buildFailed = new Command("build-failed", "Record a build failed event.");
        buildFailed.Options.Add(hostOption);
        buildFailed.Options.Add(labelOption);
        var failedExitCode = new Option<int?>("--exit-code") { Description = "Build exit code." };
        var failedDuration = new Option<long?>("--duration") { Description = "Duration in milliseconds." };
        buildFailed.Options.Add(failedExitCode);
        buildFailed.Options.Add(failedDuration);
        buildFailed.SetAction(async parseResult => await HandleEventAction(parseResult, OwsEventType.BuildFailed,
            hostOption, labelOption, failedExitCode, failedDuration));

        var testExecuted = new Command("test-executed", "Record a test executed event.");
        testExecuted.Options.Add(hostOption);
        testExecuted.Options.Add(labelOption);
        var testExitCode = new Option<int?>("--exit-code") { Description = "Test execution exit code." };
        var testDuration = new Option<long?>("--duration") { Description = "Duration in milliseconds." };
        testExecuted.Options.Add(testExitCode);
        testExecuted.Options.Add(testDuration);
        testExecuted.SetAction(async parseResult => await HandleEventAction(parseResult, OwsEventType.TestExecuted,
            hostOption, labelOption, testExitCode, testDuration));

        var programExecuted = new Command("program-executed", "Record a program executed event.");
        programExecuted.Options.Add(hostOption);
        programExecuted.Options.Add(labelOption);
        var programExitCode = new Option<int?>("--exit-code") { Description = "Program execution exit code." };
        var programDuration = new Option<long?>("--duration") { Description = "Duration in milliseconds." };
        programExecuted.Options.Add(programExitCode);
        programExecuted.Options.Add(programDuration);
        programExecuted.SetAction(async parseResult => await HandleEventAction(parseResult,
            OwsEventType.ProgramExecuted, hostOption, labelOption, programExitCode, programDuration));

        command.Subcommands.Add(buildStarted);
        command.Subcommands.Add(buildSucceeded);
        command.Subcommands.Add(buildFailed);
        command.Subcommands.Add(testExecuted);
        command.Subcommands.Add(programExecuted);

        return command;
    }

    /// <summary>
    /// Handles recording a specific timeline event from parsed command arguments.
    /// </summary>
    /// <param name="parseResult">The CLI parse result.</param>
    /// <param name="eventType">The type of the event to record.</param>
    /// <param name="hostOption">The host option definition.</param>
    /// <param name="labelOption">The label option definition.</param>
    /// <param name="exitCodeOption">The exit code option definition, if any.</param>
    /// <param name="durationOption">The duration option definition, if any.</param>
    /// <returns>The execution exit code (0 for success, 1 for failure).</returns>
    private static async Task<int> HandleEventAction(
        ParseResult parseResult,
        OwsEventType eventType,
        Option<string?> hostOption,
        Option<string?> labelOption,
        Option<int?>? exitCodeOption = null,
        Option<long?>? durationOption = null) {
        var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
        var host = parseResult.GetValue(hostOption)
                   ?? Environment.GetEnvironmentVariable("OWS_HOST")
                   ?? "cli";
        var label = parseResult.GetValue(labelOption);
        var exitCode = exitCodeOption != null ? parseResult.GetValue(exitCodeOption) : null;
        var duration = durationOption != null ? parseResult.GetValue(durationOption) : null;

        var response = new OwsCliResponse();
        try {
            var projectRoot = Directory.GetCurrentDirectory();
            var manager = new OwsWatchSessionManager();
            if (!manager.IsProjectInitialized(projectRoot)) {
                throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
            }

            await manager.EmitGenericEventAsync(projectRoot, eventType, host, label, exitCode, duration);
            response.Success = true;
            response.ProjectRoot = projectRoot;
            response.Message = $"Successfully recorded {eventType} event in the local timeline.";
        } catch (Exception ex) {
            response.Success = false;
            response.Errors.Add(OwsCommandFactory.GetFriendlyErrorMessage(ex));
        }

        OwsCommandFactory.PrintResult(response, useJson);
        return response.Success ? 0 : 1;
    }
}
