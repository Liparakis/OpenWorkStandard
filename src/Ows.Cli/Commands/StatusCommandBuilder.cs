using System.CommandLine;
using Ows.Core;
using Ows.Core.Agent;

namespace Ows.Cli.Commands;

/// <summary>
/// Provides construction for the local tracking status command.
/// </summary>
public static class StatusCommandBuilder {
    /// <summary>
    /// Builds the status command that reports local project and Agent state.
    /// </summary>
    public static Command Build() {
        var command = new Command("status", "Show local OWS project and Agent status.");
        command.SetAction(parseResult => {
            var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
            var response = new OwsCliResponse {
                ProjectRoot = Directory.GetCurrentDirectory()
            };

            try {
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(response.ProjectRoot)) {
                    response.Status = "Not Initialized";
                    response.Errors.Add("OWS project is not initialized. Run 'ows init' first.");
                } else if (manager.DidWatcherCrash(response.ProjectRoot)) {
                    response.Status = "Error";
                    response.Errors.Add("Watcher has crashed or is not running.");
                } else if (manager.IsWatcherRunning(response.ProjectRoot)) {
                    response.Success = true;
                    response.Status = "Watching";
                    response.WatcherRunning = true;
                    response.Message = "OWS Agent is watching this project.";
                } else {
                    response.Success = true;
                    response.Status = "Ready";
                    response.Message = "OWS project is initialized; the Agent is not currently watching it.";
                }
            } catch (Exception ex) {
                response.Errors.Add(OwsCommandFactory.GetFriendlyErrorMessage(ex));
            }

            OwsCommandFactory.PrintResult(response, useJson);
            return Task.FromResult(response.Success ? 0 : 1);
        });

        return command;
    }
}
