using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Ows.Core.Init;
using Ows.Core.Notarization;
using Ows.Core.Packaging;
using Ows.Core.Events;
using Ows.Core.Hashing;

namespace Ows.Core.Agent;

/// <summary>
/// Implements the shared watcher and session lifecycle manager.
/// </summary>
public sealed partial class OwsWatchSessionManager : IOwsWatchSessionManager
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private static readonly string[] KnownWatcherProcessNames = ["ows", "dotnet", "testhost"];

    private static readonly JsonSerializerOptions ConfigSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private ITrackingAgent? _activeAgent;
    private CancellationTokenSource? _activeCts;

    /// <inheritdoc />
    public bool IsProjectInitialized(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot)) return false;
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        return Directory.Exists(localFolder) && File.Exists(Path.Combine(localFolder, "config.json"));
    }

    /// <inheritdoc />
    public void InitializeProject(string projectRoot)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var initializer = new OwsProjectInitializer();
        initializer.Initialize(projectRoot);
    }

    /// <inheritdoc />
    public async Task StartWatcherAsync(string projectRoot, bool usePolling = false, int debounceMs = 500)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        Directory.CreateDirectory(localFolder);

        var watcherJsonPath = Path.Combine(localFolder, "watcher.json");
        var stopFilePath = Path.Combine(localFolder, "watcher.stop");

        if (IsWatcherRunning(projectRoot))
        {
            throw new InvalidOperationException("Watcher is already running for this project.");
        }

        if (File.Exists(stopFilePath))
        {
            TryDeleteFile(stopFilePath);
        }

        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        var state = new WatcherProcessState
        {
            Pid = currentProcess.Id,
            StartedAt = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(watcherJsonPath, JsonSerializer.Serialize(state, SerializerOptions));

        var host = Environment.GetEnvironmentVariable("OWS_HOST") ?? "cli";
        try
        {
            await EmitProjectOpenedAsync(projectRoot, host, "user_start");
        }
        catch
        {
            // Do not crash watcher startup if event logging fails
        }

        _activeCts = new CancellationTokenSource();
        var token = _activeCts.Token;

        // Background poller for watcher.stop file
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                if (File.Exists(stopFilePath))
                {
                    await _activeCts.CancelAsync();
                    break;
                }

                try
                {
                    await Task.Delay(500, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);

        // Background poller for session heartbeats (every 30 seconds)
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(30000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (token.IsCancellationRequested)
                {
                    break;
                }

                if (File.Exists(sessionPath))
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(sessionPath, token);
                        var sessionState = JsonSerializer.Deserialize<SessionState>(content);
                        if (sessionState != null && !string.IsNullOrWhiteSpace(sessionState.VerifierUrl))
                        {
                            await SendHeartbeatAsync(projectRoot);
                        }
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                    catch (JsonException)
                    {
                        // Swallow background heartbeat loop errors to keep the watcher alive.
                        // SendHeartbeatAsync automatically writes the error flags to session.json.
                    }
                }
            }
        }, token);

        _activeAgent = new LocalTrackingAgent(new NullLogger<LocalTrackingAgent>());
        await _activeAgent.PrepareAsync(new TrackingAgentOptions
        {
            ProjectRootPath = projectRoot,
            DatabasePath = Path.Combine(localFolder, "ows.db"),
            WatcherOptions = new FileWatcherOptions
            {
                UsePollingFallback = usePolling,
                DebounceIntervalMs = debounceMs
            }
        }, token);

        try
        {
            await _activeAgent.StartAsync(token);
        }
        finally
        {
            TryDeleteFile(watcherJsonPath);
            TryDeleteFile(stopFilePath);

            _activeCts = null;
            _activeAgent = null;
        }
    }

    /// <inheritdoc />
    public async Task StopWatcherAsync(string projectRoot)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var watcherJsonPath = Path.Combine(localFolder, "watcher.json");
        var stopFilePath = Path.Combine(localFolder, "watcher.stop");

        if (!File.Exists(watcherJsonPath)) return;

        var wasRunning = IsWatcherRunning(projectRoot);
        if (wasRunning)
        {
            var host = Environment.GetEnvironmentVariable("OWS_HOST") ?? "cli";
            try
            {
                await EmitProjectClosedAsync(projectRoot, host, "user_stop");
            }
            catch
            {
                // Do not fail watcher stop if event logging fails
            }
        }

        try
        {
            await File.WriteAllTextAsync(stopFilePath, "stop");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        // Wait up to 3 seconds for clean exit
        for (var i = 0; i < 30; i++)
        {
            if (!File.Exists(watcherJsonPath))
            {
                break;
            }

            await Task.Delay(100);
        }

        if (File.Exists(watcherJsonPath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(watcherJsonPath);
                var state = JsonSerializer.Deserialize<WatcherProcessState>(content);
                if (state != null)
                {
                    var proc = System.Diagnostics.Process.GetProcessById(state.Pid);
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                }
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            TryDeleteFile(watcherJsonPath);
        }

        if (File.Exists(stopFilePath))
        {
            TryDeleteFile(stopFilePath);
        }
    }

    /// <inheritdoc />
    public bool IsWatcherRunning(string projectRoot)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var watcherJsonPath = Path.Combine(localFolder, "watcher.json");
        if (!File.Exists(watcherJsonPath)) return false;

        try
        {
            var content = File.ReadAllText(watcherJsonPath);
            var state = JsonSerializer.Deserialize<WatcherProcessState>(content);
            if (state == null) return false;

            var proc = System.Diagnostics.Process.GetProcessById(state.Pid);
            if (proc.HasExited) return false;

            var procName = proc.ProcessName.ToLowerInvariant();
            return KnownWatcherProcessNames.Any(procName.Contains);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool DidWatcherCrash(string projectRoot)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var watcherJsonPath = Path.Combine(localFolder, "watcher.json");
        if (!File.Exists(watcherJsonPath)) return false;
        return !IsWatcherRunning(projectRoot);
    }

    private void EnsureProjectDirectoryExists(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            throw new DirectoryNotFoundException($"Project directory not found at: {projectRoot}");
        }
    }

    /// <inheritdoc />
    public async Task<string> StartSessionAsync(string projectRoot, string? verifierUrlOverride = null)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        Directory.CreateDirectory(localFolder);

        var config = GetProjectConfig(projectRoot) ?? new OwsProjectConfig();
        var verifierUrl = verifierUrlOverride ?? config.VerifierUrl;

        string sessionIdVal;
        if (string.IsNullOrWhiteSpace(verifierUrl))
        {
            var inMemory = new InMemoryReceiptService();
            var sessId = inMemory.StartSession();
            sessionIdVal = sessId.Value;
        }
        else
        {
            using var httpClient = CreateHttpClient(verifierUrl);
            var transport = new HttpsReceiptTransport(httpClient, (_, _) => new Checkpoint())
            {
                StartSessionRequest = new StartSessionRequest
                {
                    InstitutionId = config.InstitutionId,
                    AssessmentId = config.AssessmentId,
                    StudentUserId = config.StudentUserId,
                    CourseOfferingId = config.CourseOfferingId
                }
            };
            var sessId = await transport.StartSessionAsync(CancellationToken.None);
            sessionIdVal = sessId.Value;
        }

        var sessionState = new SessionState
        {
            SessionId = sessionIdVal,
            VerifierUrl = verifierUrl,
            InstitutionId = config.InstitutionId,
            AssessmentId = config.AssessmentId,
            StudentUserId = config.StudentUserId,
            CourseOfferingId = config.CourseOfferingId
        };

        await File.WriteAllTextAsync(
            Path.Combine(localFolder, OwsConstants.SessionFileName),
            JsonSerializer.Serialize(sessionState, SerializerOptions));

        await File.WriteAllTextAsync(
            Path.Combine(localFolder, OwsConstants.ReceiptsFileName),
            JsonSerializer.Serialize(
                new ReceiptChain { SessionId = new AssessmentSessionId(sessionIdVal), Receipts = [] },
                SerializerOptions));

        return sessionIdVal;
    }

    /// <inheritdoc />
    public async Task SendHeartbeatAsync(string projectRoot, string? verifierUrlOverride = null)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        if (!File.Exists(sessionPath))
        {
            throw new InvalidOperationException("No active OWS session. Start a session first.");
        }

        var state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(sessionPath))
                    ?? throw new JsonException("Session state is corrupt.");

        var verifierUrl = verifierUrlOverride ?? state.VerifierUrl;
        if (string.IsNullOrWhiteSpace(verifierUrl))
        {
            throw new InvalidOperationException("No remote verifier URL configured for this session.");
        }

        var timelinePath = Path.Combine(localFolder, OwsConstants.TimelineFileName);
        var lastEventHash = File.Exists(timelinePath)
            ? OwsEventChain.ReadLastEventHash(timelinePath)
            : null;

        try
        {
            using var httpClient = CreateHttpClient(verifierUrl);
            var payload = new
            {
                LastKnownEventHash = lastEventHash
            };

            using var response = await httpClient.PostAsJsonAsync($"sessions/{state.SessionId}/heartbeat", payload);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new UnauthorizedAccessException("Verifier returned unauthorized or forbidden status code.");
            }

            response.EnsureSuccessStatusCode();

            var heartbeatResponse = await response.Content.ReadFromJsonAsync<SessionHeartbeatResponse>()
                                    ?? throw new InvalidOperationException(
                                        "Verifier returned an invalid heartbeat response.");

            var isDegraded = heartbeatResponse.SessionTrustState == "Degraded";

            var updatedState = state with
            {
                LastHeartbeatAt = DateTimeOffset.UtcNow,
                IsVerifierOffline = false,
                IsHeartbeatFailing = false,
                IsDegraded = isDegraded,
                LastHeartbeatError = null
            };
            await File.WriteAllTextAsync(sessionPath, JsonSerializer.Serialize(updatedState, SerializerOptions));
        }
        catch (HttpRequestException ex)
        {
            var updatedState = state with
            {
                IsVerifierOffline = true,
                IsHeartbeatFailing = false,
                LastHeartbeatError = ex.Message
            };
            await File.WriteAllTextAsync(sessionPath, JsonSerializer.Serialize(updatedState, SerializerOptions));
            throw;
        }
        catch (Exception ex)
        {
            var updatedState = state with
            {
                IsVerifierOffline = false,
                IsHeartbeatFailing = true,
                IsDegraded = ex is not UnauthorizedAccessException && state.IsDegraded,
                LastHeartbeatError = ex.Message
            };
            await File.WriteAllTextAsync(sessionPath, JsonSerializer.Serialize(updatedState, SerializerOptions));
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> AddCheckpointAsync(string projectRoot)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        var receiptsPath = Path.Combine(localFolder, OwsConstants.ReceiptsFileName);
        var timelinePath = Path.Combine(localFolder, OwsConstants.TimelineFileName);

        if (!File.Exists(sessionPath))
        {
            throw new InvalidOperationException("No active OWS session. Start a session first.");
        }

        var state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(sessionPath))
                    ?? throw new JsonException("Session state is corrupt.");

        var sessionId = new AssessmentSessionId(state.SessionId);
        var receiptChain = File.Exists(receiptsPath)
            ? JsonSerializer.Deserialize<ReceiptChain>(await File.ReadAllTextAsync(receiptsPath))
              ?? throw new JsonException("Receipt chain is corrupt.")
            : new ReceiptChain { SessionId = sessionId, Receipts = [] };

        CheckpointReceipt receipt;
        ReceiptChain updatedReceiptChain;

        if (string.IsNullOrWhiteSpace(state.VerifierUrl))
        {
            var checkpoint = Checkpoint.FromTimeline(timelinePath, sessionId, receiptChain.Receipts.Count + 1);
            var service = new InMemoryReceiptService();
            service.RestoreSession(sessionId, receiptChain.Receipts);
            receipt = service.SubmitCheckpoint(checkpoint);
            updatedReceiptChain = service.GetReceiptChain(sessionId);
        }
        else
        {
            try
            {
                using var httpClient = CreateHttpClient(state.VerifierUrl);
                var transport = new HttpsReceiptTransport(
                    httpClient,
                    (activeSessionId, sequenceNumber) =>
                        Checkpoint.FromTimeline(timelinePath, activeSessionId, sequenceNumber));
                transport.RestoreSession(sessionId, receiptChain.Receipts.Count + 1);
                receipt = await transport.SendCheckpointAsync(CancellationToken.None);
                updatedReceiptChain = await transport.GetReceiptsAsync(CancellationToken.None);
            }
            catch (HttpRequestException ex)
            {
                var updatedState = state with
                {
                    IsVerifierOffline = true,
                    IsHeartbeatFailing = false,
                    LastHeartbeatError = ex.Message
                };
                await File.WriteAllTextAsync(sessionPath, JsonSerializer.Serialize(updatedState, SerializerOptions));
                throw;
            }
            catch (Exception ex)
            {
                var updatedState = state with
                {
                    IsVerifierOffline = false,
                    IsHeartbeatFailing = true,
                    LastHeartbeatError = ex.Message
                };
                await File.WriteAllTextAsync(sessionPath, JsonSerializer.Serialize(updatedState, SerializerOptions));
                throw;
            }
        }

        await File.WriteAllTextAsync(receiptsPath, JsonSerializer.Serialize(updatedReceiptChain, SerializerOptions));

        var finalState = state with
        {
            LastCheckpointAt = DateTimeOffset.UtcNow,
            IsVerifierOffline = false,
            IsHeartbeatFailing = false,
            LastHeartbeatError = null
        };
        await File.WriteAllTextAsync(sessionPath, JsonSerializer.Serialize(finalState, SerializerOptions));

        return receipt.ReceiptHash;
    }

    /// <inheritdoc />
    public string? GetCurrentSessionId(string projectRoot)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        if (!File.Exists(sessionPath)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(sessionPath));
            return state?.SessionId;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public string? GetVerifierUrl(string projectRoot)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        if (File.Exists(sessionPath))
        {
            try
            {
                var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(sessionPath));
                if (!string.IsNullOrWhiteSpace(state?.VerifierUrl)) return state.VerifierUrl;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (JsonException)
            {
            }
        }

        var config = GetProjectConfig(projectRoot);
        return config?.VerifierUrl;
    }

    /// <inheritdoc />
    public OwsProjectConfig? GetProjectConfig(string projectRoot)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var configPath = Path.Combine(localFolder, "config.json");
        if (!File.Exists(configPath)) return null;

        try
        {
            return JsonSerializer.Deserialize<OwsProjectConfig>(File.ReadAllText(configPath), ConfigSerializerOptions);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void SaveProjectConfig(string projectRoot, OwsProjectConfig config)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        Directory.CreateDirectory(localFolder);
        var configPath = Path.Combine(localFolder, "config.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, ConfigSerializerOptions));
    }

    /// <inheritdoc />
    public DateTimeOffset? GetLastCheckpointAt(string projectRoot)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        if (!File.Exists(sessionPath)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(sessionPath));
            return state?.LastCheckpointAt;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <inheritdoc />
    public DateTimeOffset? GetLastHeartbeatAt(string projectRoot)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        if (!File.Exists(sessionPath)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(sessionPath));
            return state?.LastHeartbeatAt;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<string> PackageProjectAsync(string projectRoot)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var packagePath = Path.Combine(projectRoot,
            $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}");
        var builder = new OwsPackageBuilder();
        var result = await builder.CreatePackageAsync(new PackageCreationRequest
        {
            ProjectRootPath = projectRoot,
            OutputPackagePath = packagePath
        }, CancellationToken.None);

        if (!result.Created)
        {
            throw new InvalidOperationException("Packaging failed.");
        }

        // Emit PackageCreated event locally
        try
        {
            var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
            string? sessionId = null;
            if (File.Exists(sessionPath))
            {
                var state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(sessionPath));
                sessionId = state?.SessionId;
            }

            var packageHash = string.Empty;
            long packageSize = 0;
            var artifactCount = 0;

            if (File.Exists(packagePath))
            {
                packageSize = new FileInfo(packagePath).Length;
                packageHash = new Sha256HashService().ComputeHash(await File.ReadAllBytesAsync(packagePath));
                using var archive = ZipFile.OpenRead(packagePath);
                artifactCount = archive.Entries.Count(entry =>
                    entry.FullName.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase));
            }

            var metadata = new Dictionary<string, string>
            {
                { "packagePath", Path.GetFileName(packagePath) },
                { "packageHash", packageHash },
                { "packageSize", packageSize.ToString() },
                { "artifactCount", artifactCount.ToString() },
                { "createdAt", DateTimeOffset.UtcNow.ToString("o") }
            };
            if (sessionId is not null)
            {
                metadata["sessionId"] = sessionId;
            }

            var host = Environment.GetEnvironmentVariable("OWS_HOST") ?? "cli";
            await AppendEventToTimelineAsync(projectRoot, OwsEventType.PackageCreated, Path.GetFileName(packagePath),
                host, 0, metadata);
        }
        catch
        {
            // Failures in writing the event should not fail the package command itself
        }

        return packagePath;
    }

    /// <inheritdoc />
    public async Task<string> UploadPackageAsync(string projectRoot, string packagePath,
        string? verifierUrlOverride = null)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);

        string? verifierUrl = verifierUrlOverride;
        string? sessionId = null;
        string? institutionId = null;
        string? assessmentId = null;
        string? studentUserId = null;

        if (File.Exists(sessionPath))
        {
            try
            {
                var state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(sessionPath));
                if (state is not null)
                {
                    verifierUrl ??= state.VerifierUrl;
                    sessionId = state.SessionId;
                    institutionId = state.InstitutionId;
                    assessmentId = state.AssessmentId;
                    studentUserId = state.StudentUserId;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (JsonException)
            {
            }
        }

        var config = GetProjectConfig(projectRoot);
        if (config != null)
        {
            verifierUrl ??= config.VerifierUrl;
            institutionId ??= config.InstitutionId;
            assessmentId ??= config.AssessmentId;
            studentUserId ??= config.StudentUserId;
        }

        if (string.IsNullOrWhiteSpace(verifierUrl))
        {
            throw new InvalidOperationException("No remote verifier URL configured for package upload.");
        }

        using var httpClient = CreateHttpClient(verifierUrl);
        httpClient.Timeout = TimeSpan.FromSeconds(60);

        using var form = new MultipartFormDataContent();

        var fileStream = File.OpenRead(packagePath);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
        form.Add(fileContent, "file", Path.GetFileName(packagePath));

        if (!string.IsNullOrWhiteSpace(sessionId)) form.Add(new StringContent(sessionId), "sessionId");
        if (!string.IsNullOrWhiteSpace(institutionId)) form.Add(new StringContent(institutionId), "institutionId");
        if (!string.IsNullOrWhiteSpace(assessmentId)) form.Add(new StringContent(assessmentId), "assessmentId");
        if (!string.IsNullOrWhiteSpace(studentUserId)) form.Add(new StringContent(studentUserId), "studentUserId");

        var response = await httpClient.PostAsync("packages/upload", form);

        if (response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge)
        {
            throw new InvalidOperationException("Package is too large for the verifier server.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new UnauthorizedAccessException("Upload unauthorized: Invalid or expired API key.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new ArgumentException($"Invalid package shape: {errorBody}");
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<VerifierPackageSubmissionResponse>()
                   ?? throw new InvalidOperationException("Verifier returned an invalid upload response.");

        if (File.Exists(sessionPath))
        {
            try
            {
                var state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(sessionPath));
                if (state is not null)
                {
                    var updatedState = state with { LastPackageId = body.SubmissionId };
                    await File.WriteAllTextAsync(sessionPath,
                        JsonSerializer.Serialize(updatedState, SerializerOptions));
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (JsonException)
            {
            }
        }

        return body.SubmissionId;
    }

    /// <inheritdoc />
    public async Task<string> QueryPackageStatusAsync(string projectRoot, string packageId,
        string? verifierUrlOverride = null)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var verifierUrl = verifierUrlOverride ?? GetVerifierUrl(projectRoot);
        if (string.IsNullOrWhiteSpace(verifierUrl))
        {
            throw new InvalidOperationException("No remote verifier URL configured for status query.");
        }

        using var httpClient = CreateHttpClient(verifierUrl);
        var response = await httpClient.GetAsync($"packages/{packageId}");

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new UnauthorizedAccessException("Query unauthorized: Invalid or expired API key.");
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private static HttpClient CreateHttpClient(string verifierUrl)
    {
        var client = new HttpClient { BaseAddress = new Uri(verifierUrl, UriKind.Absolute) };
        var apiKey = Environment.GetEnvironmentVariable("OWS_VERIFIER_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            client.DefaultRequestHeaders.Add("X-OWS-Verifier-Key", apiKey);
        }

        return client;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <inheritdoc />
    public async Task EmitProjectOpenedAsync(string projectRoot, string host, string? reason = null)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        string? sessionId = null;
        if (File.Exists(sessionPath))
        {
            try
            {
                var state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(sessionPath));
                sessionId = state?.SessionId;
            }
            catch
            {
                /*ignored*/
            }
        }

        var hashService = new Sha256HashService();
        var safeProjectIdentifier = hashService.ComputeHash(projectRoot);

        var metadata = new Dictionary<string, string>
        {
            { "host", host },
            { "projectIdentifier", safeProjectIdentifier }
        };
        if (reason is not null)
        {
            metadata["reason"] = reason;
        }

        if (sessionId is not null)
        {
            metadata["sessionId"] = sessionId;
        }

        await AppendEventToTimelineAsync(projectRoot, OwsEventType.ProjectOpened, null, host, null, metadata);
    }

    /// <inheritdoc />
    public async Task EmitProjectClosedAsync(string projectRoot, string host, string? reason = null)
    {
        EnsureProjectDirectoryExists(projectRoot);
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        string? sessionId = null;
        if (File.Exists(sessionPath))
        {
            try
            {
                var state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(sessionPath));
                sessionId = state?.SessionId;
            }
            catch
            {
                /*ignored*/
            }
        }

        var hashService = new Sha256HashService();
        var safeProjectIdentifier = hashService.ComputeHash(projectRoot);

        var metadata = new Dictionary<string, string>
        {
            { "host", host },
            { "projectIdentifier", safeProjectIdentifier }
        };
        if (reason is not null)
        {
            metadata["reason"] = reason;
        }

        if (sessionId is not null)
        {
            metadata["sessionId"] = sessionId;
        }

        await AppendEventToTimelineAsync(projectRoot, OwsEventType.ProjectClosed, null, host, null, metadata);
    }

    /// <inheritdoc />
    public async Task EmitGenericEventAsync(
        string projectRoot,
        OwsEventType eventType,
        string host,
        string? label = null,
        int? exitCode = null,
        long? durationMs = null)
    {
        EnsureProjectDirectoryExists(projectRoot);

        var allowedEventTypes = new[]
        {
            OwsEventType.BuildStarted,
            OwsEventType.BuildSucceeded,
            OwsEventType.BuildFailed,
            OwsEventType.TestExecuted,
            OwsEventType.ProgramExecuted
        };

        if (Array.IndexOf(allowedEventTypes, eventType) < 0)
        {
            throw new ArgumentException($"Event type '{eventType}' is not allowed for generic emission in v0.1.");
        }

        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, OwsConstants.SessionFileName);
        string? sessionId = null;
        if (File.Exists(sessionPath))
        {
            try
            {
                var state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(sessionPath));
                sessionId = state?.SessionId;
            }
            catch
            {
                /*ignored*/
            }
        }

        var metadata = new Dictionary<string, string>
        {
            { "host", host }
        };
        if (!string.IsNullOrWhiteSpace(label))
        {
            metadata["label"] = ScrubSecrets(label);
        }

        if (exitCode.HasValue)
        {
            metadata["exitCode"] = exitCode.Value.ToString();
        }

        if (durationMs.HasValue)
        {
            metadata["durationMs"] = durationMs.Value.ToString();
        }

        if (sessionId is not null)
        {
            metadata["sessionId"] = sessionId;
        }

        await AppendEventToTimelineAsync(projectRoot, eventType, null, host, null, metadata);
    }

    /// <summary>
    /// Scrub keys, tokens, passwords, and credentials from inputs.
    /// </summary>
    public static string ScrubSecrets(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // Redact values matching common key/token/password assignments or headers
        var result = MyRegex().Replace(input, "$1$2[REDACTED]");

        // Redact Bearer tokens
        result = MyRegex1().Replace(result, "$1[REDACTED]");

        return result;
    }

    private async Task AppendEventToTimelineAsync(
        string projectRoot,
        OwsEventType eventType,
        string? relativePath,
        string? toolName,
        long? bytesChanged,
        IReadOnlyDictionary<string, string> metadata)
    {
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var timelinePath = Path.Combine(localFolder, OwsConstants.TimelineFileName);

        var previousEventHash = OwsEventChain.GenesisPreviousEventHash;
        if (File.Exists(timelinePath))
        {
            previousEventHash = OwsEventChain.ReadLastEventHash(timelinePath);
        }
        else
        {
            Directory.CreateDirectory(localFolder);
        }

        var owsEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
        {
            EventType = eventType,
            ProjectId = Path.GetFileName(projectRoot),
            RelativePath = relativePath,
            ToolName = toolName,
            BytesChanged = bytesChanged,
            Metadata = metadata
        }, previousEventHash);

        await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(owsEvent)}{Environment.NewLine}");
    }

    [GeneratedRegex(
        @"(?i)(password|token|key|secret|credential|pwd|bearer|auth|signature)(\s*[:=]\s*|\s+)([a-zA-Z0-9_\-\.\~]{6,})",
        RegexOptions.None, "en-US")]
    private static partial Regex MyRegex();

    [GeneratedRegex(@"(?i)(bearer\s+)([a-zA-Z0-9_\-\.\~]{8,})", RegexOptions.None, "en-US")]
    private static partial Regex MyRegex1();
}

/// <summary>
/// State tracking helper for watcher processes.
/// </summary>
public sealed class WatcherProcessState
{
    /// <summary>Gets or sets the Process Identifier.</summary>
    public int Pid { get; init; }

    /// <summary>Gets or sets the process start timestamp.</summary>
    public DateTimeOffset StartedAt { get; init; }
}