using System.CommandLine;
using Ows.Core;
using Ows.Core.Agent;

namespace Ows.Cli.Commands;

/// <summary>
/// Provides construction for the init command.
/// </summary>
public static class InitCommandBuilder
{
    /// <summary>
    /// Builds the init command that initializes local OWS tracking metadata for a project.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build()
    {
        var command = new Command("init", "Initialize local OWS tracking metadata for a project.");
        command.SetAction(parseResult =>
        {
            var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
            var response = new OwsCliResponse();
            try
            {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                manager.InitializeProject(projectRoot);

                response.Success = true;
                response.Status = "Ready";
                response.ProjectRoot = projectRoot;
                response.Message = $"OWS initialized at {Path.Combine(projectRoot, OwsConstants.LocalFolderName)}";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Errors.Add(OwsCommandFactory.GetFriendlyErrorMessage(ex));
            }

            OwsCommandFactory.PrintResult(response, useJson);
            return Task.FromResult(response.Success ? 0 : 1);
        });

        return command;
    }
}