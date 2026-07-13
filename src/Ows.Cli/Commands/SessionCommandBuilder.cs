using System.CommandLine;
using Ows.Cli;
using Ows.Core;
using Ows.Core.Agent;
using Ows.Core.Notarization;

namespace Ows.Cli.Commands;

/// <summary>
/// Provides construction for the session command group.
/// </summary>
public static class SessionCommandBuilder {
    /// <summary>
    /// Builds the session command group to manage local OWS session state.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build() {
        var command = new Command("session", "Manage local OWS session state.");
        command.Hidden = true;
        command.Subcommands.Add(BuildSessionStartCommand());
        command.Subcommands.Add(BuildCheckpointCommand());
        command.Subcommands.Add(BuildSessionHeartbeatCommand());
        command.Subcommands.Add(BuildSessionStatusCommand());
        return command;
    }

    /// <summary>
    /// Builds the start subcommand to start a local OWS assessment session.
    /// </summary>
    /// <returns>The configured subcommand.</returns>
    private static Command BuildSessionStartCommand() {
        var command = new Command("start", "Start a local OWS assessment session.");
        var serverOption = new Option<string?>("--server") {
            Description = "Use a remote verifier base URL for receipt issuance."
        };
        command.Options.Add(serverOption);
        command.SetAction(async parseResult => {
            var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
            var serverUrl = parseResult.GetValue(serverOption);
            var response = new OwsCliResponse();
            try {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(projectRoot)) {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                var sessionId = await manager.StartSessionAsync(projectRoot, serverUrl);
                var config = manager.GetProjectConfig(projectRoot);

                response.Success = true;
                response.SessionId = sessionId;
                response.ProjectRoot = projectRoot;
                response.VerifierUrl = serverUrl ?? config?.VerifierUrl;
                if (config != null) {
                    response.InstitutionId = config.InstitutionId;
                    response.AssessmentId = config.AssessmentId;
                    response.StudentUserId = config.StudentUserId;
                    response.CourseOfferingId = config.CourseOfferingId;
                }

                response.Status = "Session active";
                response.Message = $"OWS session started: {sessionId}";
            } catch (Exception ex) {
                response.Success = false;
                response.Errors.Add(OwsCommandFactory.GetFriendlyErrorMessage(ex));
            }

            OwsCommandFactory.PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        return command;
    }

    /// <summary>
    /// Builds the status subcommand to show active OWS session state details.
    /// </summary>
    /// <returns>The configured subcommand.</returns>
    private static Command BuildSessionStatusCommand() {
        var command = new Command("status", "Show active OWS session state details.");
        command.SetAction(parseResult => {
            var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
            var response = new OwsCliResponse();
            try {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(projectRoot)) {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                var sessionId = manager.GetCurrentSessionId(projectRoot);
                if (sessionId == null) {
                    throw new InvalidOperationException("No active OWS session. Run 'ows session start' first.");
                }

                var config = manager.GetProjectConfig(projectRoot);

                response.Success = true;
                response.SessionId = sessionId;
                response.ProjectRoot = projectRoot;
                response.VerifierUrl = manager.GetVerifierUrl(projectRoot);
                if (config != null) {
                    response.InstitutionId = config.InstitutionId;
                    response.AssessmentId = config.AssessmentId;
                    response.StudentUserId = config.StudentUserId;
                    response.CourseOfferingId = config.CourseOfferingId;
                }

                response.LastCheckpointAt = manager.GetLastCheckpointAt(projectRoot);
                response.LastHeartbeatAt = manager.GetLastHeartbeatAt(projectRoot);
                response.Status = "Session active";
                response.Message = $"Active session ID: {sessionId}";
            } catch (Exception ex) {
                response.Success = false;
                response.Errors.Add(OwsCommandFactory.GetFriendlyErrorMessage(ex));
            }

            OwsCommandFactory.PrintResult(response, useJson);
            return Task.FromResult(response.Success ? 0 : 1);
        });

        return command;
    }

    /// <summary>
    /// Builds the checkpoint subcommand to issue a local receipt for the current timeline head.
    /// </summary>
    /// <returns>The configured subcommand.</returns>
    private static Command BuildCheckpointCommand() {
        var command = new Command("checkpoint", "Issue a local receipt for the current timeline head.");
        command.SetAction(async parseResult => {
            var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
            var response = new OwsCliResponse();
            try {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(projectRoot)) {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                var receiptHash = await manager.AddCheckpointAsync(projectRoot);
                response.Success = true;
                response.SessionId = manager.GetCurrentSessionId(projectRoot);
                response.Message = $"OWS checkpoint recorded: {receiptHash}";
                response.LastCheckpointAt = manager.GetLastCheckpointAt(projectRoot);
            } catch (Exception ex) {
                response.Success = false;
                response.Errors.Add(OwsCommandFactory.GetFriendlyErrorMessage(ex));
            }

            OwsCommandFactory.PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        return command;
    }

    /// <summary>
    /// Builds the heartbeat subcommand to send a heartbeat to the verifier for the active session.
    /// </summary>
    /// <returns>The configured subcommand.</returns>
    private static Command BuildSessionHeartbeatCommand() {
        var command = new Command("heartbeat", "Send a heartbeat to the verifier for the active session.");
        var serverOption = new Option<string?>("--server") {
            Description = "Override the verifier base URL for the heartbeat."
        };
        command.Options.Add(serverOption);
        command.SetAction(async parseResult => {
            var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
            var serverUrl = parseResult.GetValue(serverOption);
            var response = new OwsCliResponse();
            try {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(projectRoot)) {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                await manager.SendHeartbeatAsync(projectRoot, serverUrl);
                response.Success = true;
                response.SessionId = manager.GetCurrentSessionId(projectRoot);
                response.LastHeartbeatAt = manager.GetLastHeartbeatAt(projectRoot);
                response.Message = "OWS session heartbeat sent successfully.";
            } catch (Exception ex) {
                response.Success = false;
                response.Errors.Add(OwsCommandFactory.GetFriendlyErrorMessage(ex));
            }

            OwsCommandFactory.PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        return command;
    }
}
