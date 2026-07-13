using System.CommandLine;
using Ows.Core;
using Ows.Core.Agent;
using Ows.Core.Packaging;

namespace Ows.Cli.Commands;

/// <summary>
/// Provides construction for the package command group.
/// </summary>
public static class PackageCommandBuilder {
    /// <summary>
    /// Builds the package command that creates an offline OWS package.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build() {
        var command = new Command("package", "Create an OWS submission package.");
        var signOption = new Option<bool>("--sign") {
            Description = "Sign the package root with the local OWS signing key."
        };
        command.Options.Add(signOption);

        // Default action: create package.
        command.SetAction(async parseResult => {
            var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
            var signPackage = parseResult.GetValue(signOption);
            var previousSigningKeyPath = Environment.GetEnvironmentVariable("OWS_PACKAGE_SIGNING_KEY_PATH");
            var response = new OwsCliResponse();
            try {
                if (signPackage) {
                    Environment.SetEnvironmentVariable(
                        "OWS_PACKAGE_SIGNING_KEY_PATH", OwsSigningKeyStore.GetDefaultKeyPath());
                }

                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsProjectAgent();
                if (!manager.IsProjectInitialized(projectRoot)) {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                var agentFlushed = await OwsAgentIpcClient.TryFlushAsync(
                    OwsProjectRegistry.GetDefaultRegistryPath(), projectRoot);
                var path = await manager.PackageProjectAsync(projectRoot);
                response.Success = true;
                response.ProjectRoot = projectRoot;
                response.Message = agentFlushed
                    ? $"OWS package created successfully after local Agent flush: {path}"
                    : $"OWS package created successfully from local state (Agent unavailable): {path}";
            } catch (Exception ex) {
                response.Success = false;
                response.Errors.Add(OwsCommandFactory.GetFriendlyErrorMessage(ex));
            } finally {
                Environment.SetEnvironmentVariable("OWS_PACKAGE_SIGNING_KEY_PATH", previousSigningKeyPath);
            }

            OwsCommandFactory.PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        return command;
    }
}
