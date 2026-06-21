using System.CommandLine;
using System.Text.Json;
using Ows.Core;
using Ows.Core.Agent;
using Ows.Core.Notarization;

namespace Ows.Cli.Commands;

/// <summary>
/// Provides construction for the status command.
/// </summary>
public static class StatusCommandBuilder
{
    /// <summary>
    /// Builds the status command that shows current OWS tracking and session status.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build()
    {
        var command = new Command("status", "Show current OWS tracking and session status.");
        command.SetAction(parseResult =>
        {
            var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
            var response = new OwsCliResponse();
            try
            {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();

                response.ProjectRoot = projectRoot;
                if (!manager.IsProjectInitialized(projectRoot))
                {
                    response.Success = false;
                    response.Errors.Add("OWS project is not initialized. Run 'ows init' first.");
                    response.Status = "Not Initialized";
                }
                else
                {
                    var config = manager.GetProjectConfig(projectRoot);
                    var sessId = manager.GetCurrentSessionId(projectRoot);
                    var watcherRunning = manager.IsWatcherRunning(projectRoot);
                    var watcherCrashed = manager.DidWatcherCrash(projectRoot);

                    response.Success = true;
                    response.SessionId = sessId;
                    response.WatcherRunning = watcherRunning;

                    if (config != null)
                    {
                        response.VerifierUrl = config.VerifierUrl;
                        response.InstitutionId = config.InstitutionId;
                        response.AssessmentId = config.AssessmentId;
                        response.StudentUserId = config.StudentUserId;
                        response.CourseOfferingId = config.CourseOfferingId;
                    }

                    response.LastCheckpointAt = manager.GetLastCheckpointAt(projectRoot);
                    response.LastHeartbeatAt = manager.GetLastHeartbeatAt(projectRoot);

                    bool isOffline = false;
                    bool isFailing = false;
                    bool isDegraded = false;
                    string? lastErr = null;

                    var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
                    var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
                    if (File.Exists(sessionPath))
                    {
                        try
                        {
                            var content = File.ReadAllText(sessionPath);
                            var state = JsonSerializer.Deserialize<SessionState>(content);
                            if (state != null)
                            {
                                isOffline = state.IsVerifierOffline;
                                isFailing = state.IsHeartbeatFailing;
                                isDegraded = state.IsDegraded;
                                lastErr = state.LastHeartbeatError;
                            }
                        }
                        catch
                        {
                            /* ignored */
                        }
                    }

                    if (watcherCrashed)
                    {
                        response.Status = "Error";
                        response.Errors.Add("Watcher has crashed or is not running.");
                    }
                    else if (isOffline)
                    {
                        response.Status = "VerifierOffline";
                        response.Errors.Add(lastErr ?? "Verifier server is offline or unreachable.");
                    }
                    else if (isFailing)
                    {
                        response.Status = "HeartbeatFailing";
                        response.Errors.Add(lastErr ?? "Verifier session heartbeats are failing.");
                    }
                    else if (isDegraded)
                    {
                        response.Status = "Degraded";
                        response.Message = "OWS session is active but degraded (lease gap detected).";
                    }
                    else if (watcherRunning)
                    {
                        if (string.IsNullOrWhiteSpace(response.VerifierUrl))
                        {
                            response.Status = "WatchingLocalOnly";
                            response.Message = "Watcher is running in local-only mode.";
                        }
                        else
                        {
                            response.Status = "SessionActive";
                            response.Message = "Watcher is running and session is active.";
                        }
                    }
                    else if (sessId != null)
                    {
                        response.Status = "SessionActive";
                        response.Message = "Session is active but watcher is not running.";
                    }
                    else
                    {
                        response.Status = "Ready";
                        response.Message = "OWS is ready.";
                    }
                }
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