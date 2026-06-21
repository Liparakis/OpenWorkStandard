using System.CommandLine;
using Ows.Core.Agent;

namespace Ows.Cli.Commands;

/// <summary>
/// Provides construction for the watch command group.
/// </summary>
public static class WatchCommandBuilder
{
    /// <summary>
    /// Builds the watch command group that manages the persistent local file-system tracking agent.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build()
    {
        var command = new Command("watch", "Start the persistent local file-system tracking agent.");

        var pollOption = new Option<bool>("--poll")
        {
            Description = "Use the polling fallback instead of native OS file-system signals."
        };
        var debounceOption = new Option<int>("--debounce")
        {
            Description = "Minimum quiet time in milliseconds before a detected change is recorded. Defaults to 500.",
            DefaultValueFactory = _ => 500
        };

        command.Options.Add(pollOption);
        command.Options.Add(debounceOption);

        var startCommand = new Command("start", "Start the file watcher in background.");
        startCommand.Options.Add(pollOption);
        startCommand.Options.Add(debounceOption);

        var stopCommand = new Command("stop", "Stop the running file watcher.");

        command.Subcommands.Add(startCommand);
        command.Subcommands.Add(stopCommand);

        // Standard watch/watch start action
        var watchAction = async (ParseResult parseResult) =>
        {
            var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
            var usePolling = parseResult.GetValue(pollOption);
            var debounceMs = parseResult.GetValue(debounceOption);
            var projectRoot = Directory.GetCurrentDirectory();
            var manager = new OwsWatchSessionManager();

            var response = new OwsCliResponse();
            try
            {
                if (!manager.IsProjectInitialized(projectRoot))
                {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                response.Success = true;
                response.Status = "Watching";
                response.WatcherRunning = true;
                response.ProjectRoot = projectRoot;
                response.SessionId = manager.GetCurrentSessionId(projectRoot);
                response.Message = $"OWS watching {projectRoot}";

                if (useJson)
                {
                    // Print JSON status immediately and keep running
                    OwsCommandFactory.PrintResult(response, true);
                }
                else
                {
                    Console.WriteLine(response.Message);
                    Console.WriteLine("Press Ctrl+C to stop.");
                }

                await manager.StartWatcherAsync(projectRoot, usePolling, debounceMs);
                return 0;
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Errors.Add(OwsCommandFactory.GetFriendlyErrorMessage(ex));
                OwsCommandFactory.PrintResult(response, useJson);
                return 1;
            }
        };

        command.SetAction(watchAction);
        startCommand.SetAction(watchAction);

        stopCommand.SetAction(async parseResult =>
        {
            var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
            var projectRoot = Directory.GetCurrentDirectory();
            var manager = new OwsWatchSessionManager();
            var response = new OwsCliResponse();

            try
            {
                if (!manager.IsProjectInitialized(projectRoot))
                {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                await manager.StopWatcherAsync(projectRoot);
                response.Success = true;
                response.WatcherRunning = false;
                response.Status = "Ready";
                response.Message = "OWS watch stopped.";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Errors.Add(OwsCommandFactory.GetFriendlyErrorMessage(ex));
            }

            OwsCommandFactory.PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        return command;
    }
}