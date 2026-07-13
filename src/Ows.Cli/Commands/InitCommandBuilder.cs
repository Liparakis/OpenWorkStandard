using System.CommandLine;
using Ows.Core;
using Ows.Core.Agent;

namespace Ows.Cli.Commands;

/// <summary>
///     Provides construction for the init command.
/// </summary>
public static class InitCommandBuilder {
    /// <summary>
    ///     Builds the init command that initializes local OWS tracking metadata for a project.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build() {
        var command = new Command("init", "Initialize local OWS tracking metadata for a project.");
        command.SetAction(parseResult => {
                var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
                var response = new OwsCliResponse();
                try {
                    var projectRoot = Directory.GetCurrentDirectory();
                    var manager = new OwsProjectAgent();
                    manager.InitializeProject(projectRoot);
                    var registered = new OwsProjectRegistry().Register(projectRoot);
                    var agentAvailable = OwsAgentIpcClient.TryPingAsync(OwsProjectRegistry.GetDefaultRegistryPath())
                                                          .GetAwaiter().GetResult();

                    response.Success = true;
                    response.Status = agentAvailable ? "Ready" : "AgentUnavailable";
                    response.ProjectRoot = projectRoot;
                    var registrationMessage = registered ? "registered" : "already registered";
                    response.Message = agentAvailable
                        ? $"OWS initialized and {registrationMessage} with the local Agent at {Path.Combine(projectRoot, OwsConstants.LocalFolderName)}"
                        : "OWS initialized and registered, but the local Agent is unavailable. Install or start the OWS Agent, then retry 'ows init'.";
                } catch (Exception ex) {
                    response.Success = false;
                    response.Errors.Add(OwsCommandFactory.GetFriendlyErrorMessage(ex));
                }

                OwsCommandFactory.PrintResult(response, useJson);
                return Task.FromResult(response.Success ? 0 : 1);
            }
        );

        return command;
    }
}
