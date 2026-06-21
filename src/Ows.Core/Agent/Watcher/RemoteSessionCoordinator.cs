using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ows.Core.Notarization;
using Ows.Core.Events;
using Ows.Core.Init;

namespace Ows.Core.Agent;

/// <summary>
/// Manages connection details and coordinate HTTP API communication with the remote verifier server endpoints.
/// </summary>
internal static class RemoteSessionCoordinator
{
    /// <summary>
    /// Serialization settings to format json files.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>
    /// Creates and configures an <see cref="HttpClient"/> instance with verifier API key headers if configured.
    /// </summary>
    /// <param name="verifierUrl">The absolute target verifier base URL.</param>
    /// <returns>A configured <see cref="HttpClient"/> instance.</returns>
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

    /// <summary>
    /// Starts a session locally or remotely on the verifier server.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="config">The project OWS configurations.</param>
    /// <param name="verifierUrlOverride">Optional override base URL of the verifier.</param>
    /// <returns>A task returning the started session identifier string.</returns>
    public static async Task<string> StartSessionAsync(
        string projectRoot,
        OwsProjectConfig config,
        string? verifierUrlOverride)
    {
        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
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

    /// <summary>
    /// Dispatches a periodic heartbeat message to keep the verifier session active.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="verifierUrlOverride">Optional override URL of the verifier.</param>
    /// <returns>A task representing the heartbeat send operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no session is active.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when credentials fail.</exception>
    public static async Task SendHeartbeatAsync(string projectRoot, string? verifierUrlOverride)
    {
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

    /// <summary>
    /// Generates a timeline checkpoint and obtains signed receipts from local storage or verifier server.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <returns>A task returning the generated receipt hash string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when session files are missing.</exception>
    public static async Task<string> AddCheckpointAsync(string projectRoot)
    {
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

    /// <summary>
    /// Uploads the generated zip package to the remote verifier server endpoints.
    /// </summary>
    /// <param name="projectRoot">The absolute path to the project root directory.</param>
    /// <param name="packagePath">The absolute path to the package archive file.</param>
    /// <param name="config">The configuration parameters of the project.</param>
    /// <param name="verifierUrlOverride">Optional override base URL of the verifier.</param>
    /// <returns>A task returning the submission identifier string.</returns>
    /// <exception cref="InvalidOperationException">Thrown when URL configuration is missing or upload is too large.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when authorization fails.</exception>
    /// <exception cref="ArgumentException">Thrown when package shape parameters are invalid.</exception>
    public static async Task<string> UploadPackageAsync(
        string projectRoot,
        string packagePath,
        OwsProjectConfig? config,
        string? verifierUrlOverride)
    {
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

    /// <summary>
    /// Queries the verification status of an uploaded package from the verifier server.
    /// </summary>
    /// <param name="verifierUrl">The base URL of the verifier server API.</param>
    /// <param name="packageId">The package submission identifier.</param>
    /// <returns>A task returning the response body string containing status details.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when credentials fail.</exception>
    public static async Task<string> QueryPackageStatusAsync(
        string verifierUrl,
        string packageId)
    {
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
}