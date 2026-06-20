using System.CommandLine;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using Ows.Core;
using Ows.Core.Agent;
using Ows.Core.Events;
using Ows.Core.Notarization;
using Ows.Core.Reporting;
using Ows.Core.Verification;

namespace Ows.Cli;

/// <summary>
/// Builds the OWS command-line surface.
/// </summary>
public static class OwsCommandFactory
{
    private static readonly Option<bool> JsonOption = new("--json")
    {
        Description = "Output result in JSON format.",
        Recursive = true
    };

    /// <summary>
    /// Builds the root command and placeholder subcommands.
    /// </summary>
    /// <returns>The configured root command.</returns>
    public static RootCommand BuildRootCommand()
    {
        var rootCommand = new RootCommand("Open Work Standard command-line interface")
        {
            BuildInitCommand(),
            BuildStatusCommand(),
            BuildSessionCommand(),
            BuildWatchCommand(),
            BuildPackageCommand(),
            BuildVerifyCommand(),
            BuildReportCommand(),
            BuildEventCommand()
        };

        rootCommand.Options.Add(JsonOption);

        return rootCommand;
    }

    private static Command BuildInitCommand()
    {
        var command = new Command("init", "Initialize local OWS tracking metadata for a project.");
        command.SetAction(parseResult =>
        {
            var useJson = parseResult.GetValue(JsonOption);
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
                response.Errors.Add(GetFriendlyErrorMessage(ex));
            }

            PrintResult(response, useJson);
            return Task.FromResult(response.Success ? 0 : 1);
        });

        return command;
    }

    private static Command BuildStatusCommand()
    {
        var command = new Command("status", "Show current OWS tracking and session status.");
        command.SetAction(parseResult =>
        {
            var useJson = parseResult.GetValue(JsonOption);
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
                response.Errors.Add(GetFriendlyErrorMessage(ex));
            }

            PrintResult(response, useJson);
            return Task.FromResult(response.Success ? 0 : 1);
        });

        return command;
    }

    private static Command BuildSessionCommand()
    {
        var command = new Command("session", "Manage local OWS session state.");
        command.Subcommands.Add(BuildSessionStartCommand());
        command.Subcommands.Add(BuildCheckpointCommand());
        command.Subcommands.Add(BuildSessionHeartbeatCommand());
        command.Subcommands.Add(BuildSessionStatusCommand());
        return command;
    }

    private static Command BuildSessionStartCommand()
    {
        var command = new Command("start", "Start a local OWS assessment session.");
        var serverOption = new Option<string?>("--server")
        {
            Description = "Use a remote verifier base URL for receipt issuance."
        };
        command.Options.Add(serverOption);
        command.SetAction(async parseResult =>
        {
            var useJson = parseResult.GetValue(JsonOption);
            var serverUrl = parseResult.GetValue(serverOption);
            var response = new OwsCliResponse();
            try
            {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(projectRoot))
                {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                var sessionId = await manager.StartSessionAsync(projectRoot, serverUrl);
                var config = manager.GetProjectConfig(projectRoot);

                response.Success = true;
                response.SessionId = sessionId;
                response.ProjectRoot = projectRoot;
                response.VerifierUrl = serverUrl ?? config?.VerifierUrl;
                if (config != null)
                {
                    response.InstitutionId = config.InstitutionId;
                    response.AssessmentId = config.AssessmentId;
                    response.StudentUserId = config.StudentUserId;
                    response.CourseOfferingId = config.CourseOfferingId;
                }

                response.Status = "Session active";
                response.Message = $"OWS session started: {sessionId}";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Errors.Add(GetFriendlyErrorMessage(ex));
            }

            PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        return command;
    }

    private static Command BuildSessionStatusCommand()
    {
        var command = new Command("status", "Show active OWS session state details.");
        command.SetAction(parseResult =>
        {
            var useJson = parseResult.GetValue(JsonOption);
            var response = new OwsCliResponse();
            try
            {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(projectRoot))
                {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                var sessionId = manager.GetCurrentSessionId(projectRoot);
                if (sessionId == null)
                {
                    throw new InvalidOperationException("No active OWS session. Run 'ows session start' first.");
                }

                var config = manager.GetProjectConfig(projectRoot);

                response.Success = true;
                response.SessionId = sessionId;
                response.ProjectRoot = projectRoot;
                response.VerifierUrl = manager.GetVerifierUrl(projectRoot);
                if (config != null)
                {
                    response.InstitutionId = config.InstitutionId;
                    response.AssessmentId = config.AssessmentId;
                    response.StudentUserId = config.StudentUserId;
                    response.CourseOfferingId = config.CourseOfferingId;
                }

                response.LastCheckpointAt = manager.GetLastCheckpointAt(projectRoot);
                response.LastHeartbeatAt = manager.GetLastHeartbeatAt(projectRoot);
                response.Status = "Session active";
                response.Message = $"Active session ID: {sessionId}";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Errors.Add(GetFriendlyErrorMessage(ex));
            }

            PrintResult(response, useJson);
            return Task.FromResult(response.Success ? 0 : 1);
        });

        return command;
    }

    private static Command BuildCheckpointCommand()
    {
        var command = new Command("checkpoint", "Issue a local receipt for the current timeline head.");
        command.SetAction(async parseResult =>
        {
            var useJson = parseResult.GetValue(JsonOption);
            var response = new OwsCliResponse();
            try
            {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(projectRoot))
                {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                var receiptHash = await manager.AddCheckpointAsync(projectRoot);
                response.Success = true;
                response.SessionId = manager.GetCurrentSessionId(projectRoot);
                response.Message = $"OWS checkpoint recorded: {receiptHash}";
                response.LastCheckpointAt = manager.GetLastCheckpointAt(projectRoot);
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Errors.Add(GetFriendlyErrorMessage(ex));
            }

            PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        return command;
    }

    private static Command BuildSessionHeartbeatCommand()
    {
        var command = new Command("heartbeat", "Send a heartbeat to the verifier for the active session.");
        var serverOption = new Option<string?>("--server")
        {
            Description = "Override the verifier base URL for the heartbeat."
        };
        command.Options.Add(serverOption);
        command.SetAction(async parseResult =>
        {
            var useJson = parseResult.GetValue(JsonOption);
            var serverUrl = parseResult.GetValue(serverOption);
            var response = new OwsCliResponse();
            try
            {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(projectRoot))
                {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                await manager.SendHeartbeatAsync(projectRoot, serverUrl);
                response.Success = true;
                response.SessionId = manager.GetCurrentSessionId(projectRoot);
                response.LastHeartbeatAt = manager.GetLastHeartbeatAt(projectRoot);
                response.Message = "OWS session heartbeat sent successfully.";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Errors.Add(GetFriendlyErrorMessage(ex));
            }

            PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        return command;
    }

    private static Command BuildWatchCommand()
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
            var useJson = parseResult.GetValue(JsonOption);
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
                    PrintResult(response, true);
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
                response.Errors.Add(GetFriendlyErrorMessage(ex));
                PrintResult(response, useJson);
                return 1;
            }
        };

        command.SetAction(watchAction);
        startCommand.SetAction(watchAction);

        stopCommand.SetAction(async parseResult =>
        {
            var useJson = parseResult.GetValue(JsonOption);
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
                response.Errors.Add(GetFriendlyErrorMessage(ex));
            }

            PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        return command;
    }

    private static Command BuildPackageCommand()
    {
        var command = new Command("package", "Create an OWS submission package.");

        var uploadCommand = new Command("upload", "Upload the package to a live verifier.");
        var packagePathOption = new Option<string?>("--package-path")
        {
            Description = "Path to the .owspkg file to upload."
        };
        var serverOption = new Option<string?>("--server")
        {
            Description = "Override the verifier base URL."
        };
        uploadCommand.Options.Add(packagePathOption);
        uploadCommand.Options.Add(serverOption);

        var statusCommand = new Command("status", "Query verification status of a package.");
        var packageIdOption = new Option<string?>("--package-id")
        {
            Description = "Durable package submission identifier."
        };
        statusCommand.Options.Add(packageIdOption);
        statusCommand.Options.Add(serverOption);

        command.Subcommands.Add(uploadCommand);
        command.Subcommands.Add(statusCommand);

        // Default action: Create package
        command.SetAction(async parseResult =>
        {
            var useJson = parseResult.GetValue(JsonOption);
            var response = new OwsCliResponse();
            try
            {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(projectRoot))
                {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                var path = await manager.PackageProjectAsync(projectRoot);
                response.Success = true;
                response.ProjectRoot = projectRoot;
                response.Message = $"OWS package created successfully: {path}";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Errors.Add(GetFriendlyErrorMessage(ex));
            }

            PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        uploadCommand.SetAction(async parseResult =>
        {
            var useJson = parseResult.GetValue(JsonOption);
            var pkgPath = parseResult.GetValue(packagePathOption);
            var serverUrl = parseResult.GetValue(serverOption);
            var response = new OwsCliResponse();
            try
            {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(projectRoot))
                {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                if (string.IsNullOrWhiteSpace(pkgPath))
                {
                    pkgPath = Path.Combine(projectRoot,
                        $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}");
                }

                if (!File.Exists(pkgPath))
                {
                    throw new FileNotFoundException($"Package file not found at: {pkgPath}");
                }

                var packageId = await manager.UploadPackageAsync(projectRoot, pkgPath, serverUrl);
                response.Success = true;
                response.PackageId = packageId;
                response.ProjectRoot = projectRoot;
                response.Message = $"Package uploaded successfully. Submission ID: {packageId}";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Errors.Add(GetFriendlyErrorMessage(ex));
            }

            PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        statusCommand.SetAction(async parseResult =>
        {
            var useJson = parseResult.GetValue(JsonOption);
            var pkgId = parseResult.GetValue(packageIdOption);
            var serverUrl = parseResult.GetValue(serverOption);
            var response = new OwsCliResponse();
            try
            {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(projectRoot))
                {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                if (string.IsNullOrWhiteSpace(pkgId))
                {
                    var sessionPath = Path.Combine(projectRoot, OwsConstants.LocalFolderName,
                        OwsConstants.SessionFileName);
                    if (File.Exists(sessionPath))
                    {
                        var state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(sessionPath));
                        pkgId = state?.LastPackageId;
                    }
                }

                if (string.IsNullOrWhiteSpace(pkgId))
                {
                    throw new InvalidOperationException(
                        "Package submission ID is required (no last package ID found in session).");
                }

                var jsonStatus = await manager.QueryPackageStatusAsync(projectRoot, pkgId, serverUrl);
                using var doc = JsonDocument.Parse(jsonStatus);
                var root = doc.RootElement;

                response.Success = true;
                response.PackageId = pkgId;
                response.Status = root.GetProperty("verificationStatus").GetString();
                response.TrustStatus = root.TryGetProperty("trustStatus", out var ts) ? ts.GetString() : null;
                response.Message =
                    $"Package verification status: {response.Status}. Trust status: {response.TrustStatus ?? "None"}";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Errors.Add(GetFriendlyErrorMessage(ex));
            }

            PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        return command;
    }

    private static Command BuildVerifyCommand()
    {
        var command = new Command("verify", "Verify an OWS submission package.");
        var serverOption = new Option<string?>("--server")
        {
            Description = "Cross-check packaged receipts against a live verifier API."
        };
        command.Options.Add(serverOption);
        command.SetAction(async parseResult =>
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var packagePath = Path.Combine(projectRoot,
                $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}");
            var verifier = new OwsPackageVerifier();
            var verifierUrl = parseResult.GetValue(serverOption);
            var packagedReceiptChain = ReadPackagedReceiptChain(packagePath);
            var trustedReceiptChain = string.IsNullOrWhiteSpace(verifierUrl)
                ? null
                : await FetchTrustedReceiptChainAsync(packagePath, verifierUrl, packagedReceiptChain is not null,
                    CancellationToken.None);
            var trustedSessionHead = string.IsNullOrWhiteSpace(verifierUrl)
                ? null
                : await FetchTrustedSessionHeadAsync(packagePath, verifierUrl, packagedReceiptChain is null,
                    CancellationToken.None);
            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest
                {
                    PackagePath = packagePath,
                    TrustedReceiptChain = trustedReceiptChain,
                    TrustedSessionHead = trustedSessionHead
                },
                CancellationToken.None);
            Console.WriteLine(result.Summary);
            return result.IsSuccess ? 0 : 1;
        });

        return command;
    }

    private static async Task<ReceiptChain?> FetchTrustedReceiptChainAsync(
        string packagePath,
        string verifierUrl,
        bool shouldFetchChain,
        CancellationToken cancellationToken)
    {
        if (!shouldFetchChain)
        {
            return null;
        }

        var sessionId = ReadPackagedSessionId(packagePath);
        var packagedReceiptChain = ReadPackagedReceiptChain(packagePath);
        if (sessionId is null && packagedReceiptChain is null)
        {
            throw new InvalidOperationException(
                $"The package does not contain {OwsConstants.SessionFileName} or {OwsConstants.ReceiptsFileName}, so verifier-backed verification cannot resolve a remote session.");
        }

        using var httpClient = CreateVerifierHttpClient(verifierUrl);
        var transport = new HttpsReceiptTransport(httpClient, (_, _) => new Checkpoint());
        transport.RestoreSession(
            sessionId ?? packagedReceiptChain!.SessionId,
            packagedReceiptChain!.Receipts.Count + 1);
        return await transport.GetReceiptsAsync(cancellationToken);
    }

    private static async Task<SessionHeadResponse?> FetchTrustedSessionHeadAsync(
        string packagePath,
        string verifierUrl,
        bool shouldFetchHead,
        CancellationToken cancellationToken)
    {
        if (!shouldFetchHead)
        {
            return null;
        }

        var sessionId = ReadPackagedSessionId(packagePath)
                        ?? throw new InvalidOperationException(
                            $"The package does not contain {OwsConstants.SessionFileName}, so verifier-backed verification cannot resolve a remote session head.");
        using var httpClient = CreateVerifierHttpClient(verifierUrl);
        return await httpClient.GetFromJsonAsync<SessionHeadResponse>($"sessions/{sessionId.Value}/head",
                   cancellationToken)
               ?? throw new InvalidOperationException("The verifier returned an invalid session head response.");
    }

    private static HttpClient CreateVerifierHttpClient(string verifierUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifierUrl);
        var httpClient = new HttpClient { BaseAddress = new Uri(verifierUrl, UriKind.Absolute) };
        var apiKey = Environment.GetEnvironmentVariable("OWS_VERIFIER_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            httpClient.DefaultRequestHeaders.Add("X-OWS-Verifier-Key", apiKey);
        }

        return httpClient;
    }

    private static ReceiptChain? ReadPackagedReceiptChain(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var receiptsEntry = archive.GetEntry(OwsConstants.ReceiptsFileName);
        if (receiptsEntry is null)
        {
            return null;
        }

        using var reader = new StreamReader(receiptsEntry.Open());
        return JsonSerializer.Deserialize<ReceiptChain>(reader.ReadToEnd());
    }

    private static AssessmentSessionId? ReadPackagedSessionId(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var sessionEntry = archive.GetEntry(OwsConstants.SessionFileName);
        if (sessionEntry is null)
        {
            return null;
        }

        using var reader = new StreamReader(sessionEntry.Open());
        var sessionState = JsonSerializer.Deserialize<SessionState>(reader.ReadToEnd())
                           ?? throw new JsonException("Session state deserialized to null.");
        return string.IsNullOrWhiteSpace(sessionState.SessionId)
            ? null
            : new AssessmentSessionId(sessionState.SessionId);
    }

    private static Command BuildReportCommand()
    {
        var command = new Command("report", "Generate an OWS verification report.");
        var formatOption = new Option<string?>("--format")
        {
            Description = "Report output format. Supported values: text, json."
        };
        command.Options.Add(formatOption);
        command.SetAction(async parseResult =>
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var packagePath = Path.Combine(projectRoot,
                $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}");
            var verifier = new OwsPackageVerifier();
            var verificationResult = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            if (!verificationResult.IsSuccess)
            {
                Console.WriteLine(verificationResult.Summary);
                return 1;
            }

            var generator = new OwsReportGenerator();
            var format = (parseResult.GetValue(formatOption) ?? "text").Trim().ToLowerInvariant() switch
            {
                "text" => ReportFormat.Text,
                "json" => ReportFormat.Json,
                var unsupported => throw new ArgumentException(
                    $"Unsupported report format '{unsupported}'. Supported values: text, json.")
            };
            var reportResult = await generator.GenerateAsync(
                new ReportRequest
                {
                    Format = format,
                    VerificationResult = verificationResult
                },
                CancellationToken.None);

            var extension = format switch
            {
                ReportFormat.Text => "txt",
                ReportFormat.Json => "json",
                _ => throw new NotSupportedException($"Report format '{format}' is not supported by the CLI yet.")
            };
            var reportPath = Path.Combine(projectRoot, $"{new DirectoryInfo(projectRoot).Name}.report.{extension}");
            await File.WriteAllTextAsync(reportPath, reportResult.Content);
            Console.WriteLine($"OWS report created at {reportPath}");
            return 0;
        });

        return command;
    }

    private static string GetFriendlyErrorMessage(Exception ex)
    {
        var msg = ex.Message;

        if (ex is DirectoryNotFoundException)
        {
            return msg;
        }

        if (msg.Contains("not initialized", StringComparison.OrdinalIgnoreCase))
        {
            return "OWS project is not initialized. Run 'ows init' first.";
        }

        if (msg.Contains("No remote verifier URL configured", StringComparison.OrdinalIgnoreCase))
        {
            return "No remote verifier URL configured.";
        }

        if (msg.Contains("Assessment context is missing", StringComparison.OrdinalIgnoreCase) ||
            (msg.Contains("institutionId", StringComparison.OrdinalIgnoreCase) &&
             msg.Contains("required", StringComparison.OrdinalIgnoreCase)))
        {
            return
                "Assessment context is missing (Institution ID, Assessment ID, or Student User ID). Please configure the project context.";
        }

        if (msg.Contains("OWS_VERIFIER_API_KEY", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("API Key is missing", StringComparison.OrdinalIgnoreCase))
        {
            return "No API key configured for remote verifier. Please set OWS_VERIFIER_API_KEY.";
        }

        if (ex is UnauthorizedAccessException || msg.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("forbidden", StringComparison.OrdinalIgnoreCase) || msg.Contains("401") ||
            msg.Contains("403") ||
            msg.Contains("rejected", StringComparison.OrdinalIgnoreCase))
        {
            return "API key was rejected by the verifier. Please check your credentials.";
        }

        if (msg.Contains("Watcher is already running", StringComparison.OrdinalIgnoreCase))
        {
            return "Watcher is already running for this project.";
        }

        if (msg.Contains("Watcher has crashed", StringComparison.OrdinalIgnoreCase))
        {
            return "Watcher has crashed or is not running.";
        }

        if (ex is HttpRequestException ||
            msg.Contains("connection refused", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("unreachable", StringComparison.OrdinalIgnoreCase) || msg.Contains("503") ||
            msg.Contains("offline", StringComparison.OrdinalIgnoreCase))
        {
            return "Verifier server is offline or unreachable.";
        }

        if (msg.Contains("database is locked", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("SQLite Error 5", StringComparison.OrdinalIgnoreCase))
        {
            return "Local OWS database is locked by another process.";
        }

        if (msg.Contains("Packaging failed", StringComparison.OrdinalIgnoreCase))
        {
            return msg;
        }

        if (msg.Contains("Upload failed", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("upload", StringComparison.OrdinalIgnoreCase))
        {
            return $"Package upload failed: {msg}";
        }

        return msg;
    }

    private static Command BuildEventCommand()
    {
        var command = new Command("event", "Record explicit OWS timeline events.");

        var hostOption = new Option<string?>("--host")
        {
            Description = "Host application or environment identifier (e.g. cli, vscode, rider)."
        };
        var labelOption = new Option<string?>("--label")
        {
            Description = "Safe label or command name (secrets will be redacted)."
        };

        var buildStarted = new Command("build-started", "Record a build started event.");
        buildStarted.Options.Add(hostOption);
        buildStarted.Options.Add(labelOption);
        buildStarted.SetAction(async parseResult => await HandleEventAction(parseResult, OwsEventType.BuildStarted, hostOption, labelOption));

        var buildSucceeded = new Command("build-succeeded", "Record a build succeeded event.");
        buildSucceeded.Options.Add(hostOption);
        buildSucceeded.Options.Add(labelOption);
        var succeededDuration = new Option<long?>("--duration") { Description = "Duration in milliseconds." };
        buildSucceeded.Options.Add(succeededDuration);
        buildSucceeded.SetAction(async parseResult => await HandleEventAction(parseResult, OwsEventType.BuildSucceeded, hostOption, labelOption, durationOption: succeededDuration));

        var buildFailed = new Command("build-failed", "Record a build failed event.");
        buildFailed.Options.Add(hostOption);
        buildFailed.Options.Add(labelOption);
        var failedExitCode = new Option<int?>("--exit-code") { Description = "Build exit code." };
        var failedDuration = new Option<long?>("--duration") { Description = "Duration in milliseconds." };
        buildFailed.Options.Add(failedExitCode);
        buildFailed.Options.Add(failedDuration);
        buildFailed.SetAction(async parseResult => await HandleEventAction(parseResult, OwsEventType.BuildFailed, hostOption, labelOption, failedExitCode, failedDuration));

        var testExecuted = new Command("test-executed", "Record a test executed event.");
        testExecuted.Options.Add(hostOption);
        testExecuted.Options.Add(labelOption);
        var testExitCode = new Option<int?>("--exit-code") { Description = "Test execution exit code." };
        var testDuration = new Option<long?>("--duration") { Description = "Duration in milliseconds." };
        testExecuted.Options.Add(testExitCode);
        testExecuted.Options.Add(testDuration);
        testExecuted.SetAction(async parseResult => await HandleEventAction(parseResult, OwsEventType.TestExecuted, hostOption, labelOption, testExitCode, testDuration));

        var programExecuted = new Command("program-executed", "Record a program executed event.");
        programExecuted.Options.Add(hostOption);
        programExecuted.Options.Add(labelOption);
        var programExitCode = new Option<int?>("--exit-code") { Description = "Program execution exit code." };
        var programDuration = new Option<long?>("--duration") { Description = "Duration in milliseconds." };
        programExecuted.Options.Add(programExitCode);
        programExecuted.Options.Add(programDuration);
        programExecuted.SetAction(async parseResult => await HandleEventAction(parseResult, OwsEventType.ProgramExecuted, hostOption, labelOption, programExitCode, programDuration));

        command.Subcommands.Add(buildStarted);
        command.Subcommands.Add(buildSucceeded);
        command.Subcommands.Add(buildFailed);
        command.Subcommands.Add(testExecuted);
        command.Subcommands.Add(programExecuted);

        return command;
    }

    private static async Task<int> HandleEventAction(
        ParseResult parseResult,
        OwsEventType eventType,
        Option<string?> hostOption,
        Option<string?> labelOption,
        Option<int?>? exitCodeOption = null,
        Option<long?>? durationOption = null)
    {
        var useJson = parseResult.GetValue(JsonOption);
        var host = parseResult.GetValue(hostOption)
                   ?? Environment.GetEnvironmentVariable("OWS_HOST")
                   ?? "cli";
        var label = parseResult.GetValue(labelOption);
        var exitCode = exitCodeOption != null ? parseResult.GetValue(exitCodeOption) : null;
        var duration = durationOption != null ? parseResult.GetValue(durationOption) : null;

        var response = new OwsCliResponse();
        try
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var manager = new OwsWatchSessionManager();
            if (!manager.IsProjectInitialized(projectRoot))
            {
                throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
            }

            await manager.EmitGenericEventAsync(projectRoot, eventType, host, label, exitCode, duration);
            response.Success = true;
            response.ProjectRoot = projectRoot;
            response.Message = $"Successfully recorded {eventType} event in the local timeline.";
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Errors.Add(GetFriendlyErrorMessage(ex));
        }

        PrintResult(response, useJson);
        return response.Success ? 0 : 1;
    }

    private static void PrintResult(OwsCliResponse response, bool useJson)
    {
        if (useJson)
        {
            var json = JsonSerializer.Serialize(response.ToSerializableModel(), new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(RedactApiKey(json));
        }
        else
        {
            if (response.Errors.Count > 0)
            {
                foreach (var err in response.Errors)
                {
                    Console.Error.WriteLine(RedactApiKey($"ERROR: {err}"));
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(response.Message))
                {
                    Console.WriteLine(RedactApiKey(response.Message));
                }
            }
        }
    }

    private static string RedactApiKey(string input)
    {
        var apiKey = Environment.GetEnvironmentVariable("OWS_VERIFIER_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 6)
        {
            return input;
        }

        return input.Replace(apiKey, "[REDACTED_API_KEY]");
    }
}
