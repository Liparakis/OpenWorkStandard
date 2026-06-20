using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ows.Core.Education;
using Ows.Core.Notarization;
using Ows.Core.Verification;
using Ows.Verifier.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
var storageOptions = builder.Configuration.GetSection("VerifierStorage").Get<VerifierStorageOptions>()
                     ?? new VerifierStorageOptions();
var securityOptions = builder.Configuration.GetSection("VerifierSecurity").Get<VerifierSecurityOptions>()
                      ?? new VerifierSecurityOptions();
var normalizedStorageOptions = storageOptions with
{
    JsonStorePath = string.IsNullOrWhiteSpace(storageOptions.JsonStorePath)
        ? Path.Combine(builder.Environment.ContentRootPath, ".ows-verifier", "receipts.json")
        : storageOptions.JsonStorePath,
    LocalStoragePath = string.IsNullOrWhiteSpace(storageOptions.LocalStoragePath)
        ? Path.Combine(builder.Environment.ContentRootPath, ".ows-verifier", "packages")
        : storageOptions.LocalStoragePath
};

var envString = builder.Configuration["VerifierEnvironment"]
                ?? builder.Environment.EnvironmentName
                ?? "Development";

if (!Enum.TryParse<VerifierEnvironmentMode>(envString, true, out var envMode))
{
    envMode = VerifierEnvironmentMode.Development;
}

var isProduction = envMode == VerifierEnvironmentMode.Production;
var startupWarnings = new List<string>();
var startupErrors = new List<string>();
var configuredApiKeys = BuildConfiguredApiKeys(securityOptions);

// Check API Keys
if (configuredApiKeys.Count == 0)
{
    startupWarnings.Add(
        "VerifierSecurity bootstrap API keys are not configured. Persisted API keys or unguarded local bootstrap mode will be used.");
}
else
{
    var duplicateKeys = configuredApiKeys
        .GroupBy(static key => key.Key, StringComparer.Ordinal)
        .Where(static group => group.Count() > 1)
        .Select(static group => group.Key)
        .ToArray();
    foreach (var duplicateKey in duplicateKeys)
    {
        startupErrors.Add(
            $"VerifierSecurity contains duplicate API key material for fingerprint {ReceiptChainVerifier.ComputeFingerprint(duplicateKey)}.");
    }

    foreach (var apiKey in configuredApiKeys)
    {
        if (IsWeakSecret(apiKey.Key))
        {
            if (isProduction)
            {
                startupErrors.Add(
                    $"VerifierSecurity key for role '{apiKey.Role}' is too weak/short for Production mode. It must be at least 16 characters long and not a known default.");
            }
            else
            {
                startupWarnings.Add($"VerifierSecurity key for role '{apiKey.Role}' is weak or using a dev default.");
            }
        }

        if (!IsSupportedVerifierRole(apiKey.Role))
        {
            startupErrors.Add(
                $"VerifierSecurity role '{apiKey.Role}' is not supported. Use 'Operator' or 'InstructorReviewer'.");
        }

        if (IsInstructorReviewerRole(apiKey.Role) && string.IsNullOrWhiteSpace(apiKey.InstitutionId))
        {
            startupErrors.Add("VerifierSecurity InstructorReviewer keys must set InstitutionId.");
        }
    }
}

// Check Signing Key
if (string.IsNullOrWhiteSpace(normalizedStorageOptions.ReceiptSigningKey))
{
    if (isProduction)
    {
        startupErrors.Add("VerifierStorage:ReceiptSigningKey must be configured in Production mode.");
    }
    else
    {
        startupWarnings.Add("VerifierStorage:ReceiptSigningKey is not configured. Receipts will not be signed.");
    }
}
else if (IsWeakSecret(normalizedStorageOptions.ReceiptSigningKey))
{
    if (isProduction)
    {
        startupErrors.Add(
            "VerifierStorage:ReceiptSigningKey is too weak/short for Production mode. It must be at least 16 characters long and not a known default.");
    }
    else
    {
        startupWarnings.Add("VerifierStorage:ReceiptSigningKey is weak or using a dev default.");
    }
}

// Check Storage Provider
if (string.Equals(normalizedStorageOptions.Provider, "json", StringComparison.OrdinalIgnoreCase))
{
    if (isProduction)
    {
        startupErrors.Add("JSON storage provider is not allowed in Production mode. Use 'postgres' provider.");
    }
    else
    {
        startupWarnings.Add("Using JSON file storage provider. This is only suitable for development/local use.");
    }
}

if (startupErrors.Count > 0)
{
    foreach (var error in startupErrors)
    {
        Console.Error.WriteLine($"FATAL CONFIGURATION ERROR: {error}");
    }

    throw new InvalidOperationException("Fatal configuration errors detected in Production mode. See console output.");
}

if (args.Any(static arg => string.Equals(arg, "migrate", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(arg, "--migrate", StringComparison.OrdinalIgnoreCase)))
{
    // TODO: split schema migration into a separate rollout path or startup flag before multi-replica production deployments.
    if (!string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Verifier migration is only supported when VerifierStorage:Provider=postgres.");
        return;
    }

    if (string.IsNullOrWhiteSpace(normalizedStorageOptions.PostgresConnectionString))
    {
        throw new InvalidOperationException(
            "VerifierStorage:PostgresConnectionString must be configured when VerifierStorage:Provider=postgres.");
    }

    await PostgresVerifierMigrator.MigrateAsync(normalizedStorageOptions.PostgresConnectionString);
    Console.WriteLine("Verifier schema migration complete.");
    return;
}

builder.Services.AddSingleton(normalizedStorageOptions);
builder.Services.AddSingleton(securityOptions);
builder.Services.AddSingleton<IPackageVerifier, OwsPackageVerifier>();
builder.Services.AddSingleton<IVerifierApiKeyStore>(_ =>
{
    var storeRoot = Path.GetDirectoryName(normalizedStorageOptions.JsonStorePath) ??
                    builder.Environment.ContentRootPath;
    return string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
        ? new PostgresVerifierApiKeyStore(
            !string.IsNullOrWhiteSpace(normalizedStorageOptions.PostgresConnectionString)
                ? normalizedStorageOptions.PostgresConnectionString
                : throw new InvalidOperationException(
                    "VerifierStorage:PostgresConnectionString must be configured when VerifierStorage:Provider=postgres."))
        : new JsonFileVerifierApiKeyStore(Path.Combine(storeRoot, "api_keys.json"));
});
builder.Services.AddSingleton<IPackageSubmissionStore>(_ =>
    string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
        ? new PostgresPackageSubmissionStore(normalizedStorageOptions.PostgresConnectionString)
        : new JsonFilePackageSubmissionStore(Path.Combine(
            Path.GetDirectoryName(normalizedStorageOptions.JsonStorePath) ?? builder.Environment.ContentRootPath,
            "package_submissions.json")));
builder.Services.AddSingleton<IVerifierStorage>(_ => normalizedStorageOptions.Provider switch
{
    "json" => new JsonFileVerifierStorage(
        normalizedStorageOptions.JsonStorePath,
        normalizedStorageOptions.ReceiptSigningKey),
    "postgres" => new PostgresVerifierStorage(
        !string.IsNullOrWhiteSpace(normalizedStorageOptions.PostgresConnectionString)
            ? normalizedStorageOptions.PostgresConnectionString
            : throw new InvalidOperationException(
                "VerifierStorage:PostgresConnectionString must be configured when VerifierStorage:Provider=postgres."),
        normalizedStorageOptions.ReceiptSigningKey),
    _ => throw new NotSupportedException($"Unsupported verifier storage provider: {normalizedStorageOptions.Provider}")
});
builder.Services.AddSingleton<IEducationStore>(_ =>
{
    var storePath = Path.Combine(
        Path.GetDirectoryName(normalizedStorageOptions.JsonStorePath) ?? builder.Environment.ContentRootPath,
        "education.json");
    return string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(normalizedStorageOptions.PostgresConnectionString)
        ? new PostgresEducationStore(normalizedStorageOptions.PostgresConnectionString)
        : new JsonFileEducationStore(storePath);
});

var app = builder.Build();
var requestLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Requests");
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

startupLogger.LogInformation("OWS Verifier starting up...");
startupLogger.LogInformation("Environment Mode: {EnvironmentMode}", envMode);
startupLogger.LogInformation("Storage Provider: {Provider}", normalizedStorageOptions.Provider);
startupLogger.LogInformation("API Guard: {ApiGuardStatus}", configuredApiKeys.Count == 0 ? "Disabled" : "Enabled");
if (configuredApiKeys.Count > 0)
{
    startupLogger.LogInformation("API Header Name: {HeaderName}", securityOptions.HeaderName);
    startupLogger.LogInformation("Configured API Keys: {ApiKeyCount}", configuredApiKeys.Count);
}

var keyFingerprint = ReceiptChainVerifier.ComputeFingerprint(normalizedStorageOptions.ReceiptSigningKey);
startupLogger.LogInformation("Signing Key Fingerprint: {Fingerprint}",
    string.IsNullOrWhiteSpace(keyFingerprint) ? "None (Unsigned)" : keyFingerprint);

foreach (var warning in startupWarnings)
{
    startupLogger.LogWarning("CONFIGURATION WARNING: {Warning}", warning);
}

// Eager storage initialization
if (app.Services.GetService<IVerifierStorage>() is { } storage)
{
    try
    {
        startupLogger.LogInformation("Initializing verifier storage...");
        await storage.InitializeAsync(CancellationToken.None);
        startupLogger.LogInformation("Verifier storage initialized successfully (database/migrations ready).");
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Failed to initialize verifier storage.");
        if (isProduction)
        {
            throw; // Fail startup in production
        }
    }
}

if (app.Services.GetService<IEducationStore>() is { } educationStore)
{
    try
    {
        startupLogger.LogInformation("Initializing education store...");
        await educationStore.InitializeAsync(CancellationToken.None);
        startupLogger.LogInformation("Education store initialized successfully.");
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Failed to initialize education store.");
        if (isProduction)
        {
            throw; // Fail startup in production
        }
    }
}

var apiKeyStore = app.Services.GetRequiredService<IVerifierApiKeyStore>();
try
{
    startupLogger.LogInformation("Initializing verifier API key store...");
    await apiKeyStore.InitializeAsync(CancellationToken.None);
    var hasPersistedKeys = await apiKeyStore.HasActiveKeysAsync(CancellationToken.None);
    startupLogger.LogInformation(
        "Verifier API key store initialized successfully. Persisted Keys Present: {HasPersistedKeys}",
        hasPersistedKeys);
    if (isProduction && configuredApiKeys.Count == 0 && !hasPersistedKeys)
    {
        throw new InvalidOperationException(
            "Production mode requires either bootstrap API keys or persisted verifier API keys.");
    }
}
catch (Exception ex)
{
    startupLogger.LogError(ex, "Failed to initialize verifier API key store.");
    if (isProduction)
    {
        throw;
    }
}

app.Use(async (context, next) =>
{
    var stopwatch = Stopwatch.StartNew();
    try
    {
        await next(context);
    }
    finally
    {
        stopwatch.Stop();
        requestLogger.LogInformation(
            "Verifier request {Method} {Path} returned {StatusCode} in {ElapsedMilliseconds} ms.",
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds);
    }
});

app.Use(async (context, next) =>
{
    var persistentApiKeyStore = context.RequestServices.GetRequiredService<IVerifierApiKeyStore>();
    var hasBootstrapKeys = configuredApiKeys.Count > 0;
    var hasPersistedKeys = false;
    try
    {
        hasPersistedKeys = await persistentApiKeyStore.HasActiveKeysAsync(context.RequestAborted);
    }
    catch when (!isProduction)
    {
        if (!hasBootstrapKeys)
        {
            await next(context);
            return;
        }
    }

    if (!hasBootstrapKeys && !hasPersistedKeys)
    {
        await next(context);
        return;
    }

    var suppliedKey = TryGetSuppliedApiKey(context.Request, securityOptions.HeaderName);
    var access = suppliedKey is null
        ? null
        : TryAuthenticateConfiguredApiKey(suppliedKey, configuredApiKeys)
          ?? await TryAuthenticatePersistedApiKeyAsync(persistentApiKeyStore, suppliedKey, context.RequestAborted);
    if (access is null)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        var hasHeader = context.Request.Headers.ContainsKey(securityOptions.HeaderName);
        var message = hasHeader ? "Invalid verifier API key." : "Verifier API key is required.";
        await context.Response.WriteAsync(message);
        return;
    }

    if (!await IsAuthorizedAsync(context, access, context.RequestAborted))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Verifier API key is not authorized for this resource.");
        return;
    }

    context.Items["VerifierAccessContext"] = access;
    await next(context);
});

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.MapGet("/ready",
    async (IVerifierStorage storage, VerifierStorageOptions options, CancellationToken cancellationToken) =>
    {
        try
        {
            var healthy = await storage.CheckHealthAsync(cancellationToken);
            if (!healthy)
            {
                return Results.Json(new { status = "Unhealthy", error = "Storage health check failed." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var signingStatus = !string.IsNullOrWhiteSpace(options.ReceiptSigningKey) ? "Enabled" : "Disabled";

            return Results.Ok(new
            {
                status = "Ready",
                storage = options.Provider,
                signing = signingStatus
            });
        }
        catch (Exception ex)
        {
            return Results.Json(new { status = "Unhealthy", error = ex.Message },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    });

app.MapPost("/auth/api-keys", async (VerifierApiKeyCreateRequest request, IVerifierApiKeyStore apiKeyStore,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await apiKeyStore.CreateAsync(request, cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(exception.Message);
    }
});

app.MapGet("/auth/api-keys", async (IVerifierApiKeyStore apiKeyStore, CancellationToken cancellationToken) =>
    Results.Ok(await apiKeyStore.ListAsync(cancellationToken)));

app.MapPost("/auth/api-keys/{id}/revoke", async (string id, IVerifierApiKeyStore apiKeyStore,
        CancellationToken cancellationToken) =>
    await apiKeyStore.RevokeAsync(id, cancellationToken)
        ? Results.Ok(new { keyId = id, revoked = true })
        : Results.NotFound("Unknown verifier API key."));

app.MapPost("/sessions", async (StartSessionRequest? body, IVerifierStorage storage,
    IEducationStore educationStore, CancellationToken cancellationToken) =>
{
    string? clientId = body?.StudentUserId;
    string? assessmentId = body?.AssessmentId;
    string? metadataJson = null;

    // Validate education context when any field is supplied
    if (!string.IsNullOrWhiteSpace(body?.InstitutionId)
        || !string.IsNullOrWhiteSpace(body?.AssessmentId)
        || !string.IsNullOrWhiteSpace(body?.StudentUserId))
    {
        // Validate institution exists
        if (string.IsNullOrWhiteSpace(body?.InstitutionId))
        {
            return Results.BadRequest("InstitutionId is required when education context is supplied.");
        }

        var institution = await educationStore.GetInstitutionAsync(
            new InstitutionId(body.InstitutionId), cancellationToken);
        if (institution is null)
        {
            return Results.BadRequest($"Institution '{body.InstitutionId}' not found.");
        }

        // Validate assessment belongs to the institution when supplied
        if (!string.IsNullOrWhiteSpace(body.AssessmentId))
        {
            var assessment = await educationStore.GetAssessmentAsync(
                new AssessmentId(body.AssessmentId), cancellationToken);
            if (assessment is null)
            {
                return Results.BadRequest($"Assessment '{body.AssessmentId}' not found.");
            }

            if (!string.Equals(assessment.InstitutionId.Value, body.InstitutionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(
                    $"Assessment '{body.AssessmentId}' does not belong to institution '{body.InstitutionId}'.");
            }
        }

        // Validate student exists when supplied
        if (!string.IsNullOrWhiteSpace(body.StudentUserId))
        {
            var student = await educationStore.GetUserAsync(
                new UserId(body.StudentUserId), cancellationToken);
            if (student is null)
            {
                return Results.BadRequest($"Student user '{body.StudentUserId}' not found.");
            }
        }

        metadataJson = JsonSerializer.Serialize(new
        {
            institutionId = body.InstitutionId,
            assessmentId = body.AssessmentId,
            studentUserId = body.StudentUserId,
            courseOfferingId = body.CourseOfferingId
        });
    }

    var session = await storage.CreateSessionAsync(clientId, assessmentId, metadataJson, cancellationToken);
    return Results.Ok(new StartSessionResponse { SessionId = session.Id.Value });
});

app.MapPost("/sessions/{id}/heartbeat", async (string id, SessionHeartbeatRequest request,
    IVerifierStorage storage, CancellationToken cancellationToken) =>
{
    var sessionId = new AssessmentSessionId(id);
    try
    {
        var leaseDuration = TimeSpan.FromSeconds(120);
        var session = await storage.RecordHeartbeatAsync(
            sessionId,
            request.LastKnownEventHash,
            leaseDuration,
            cancellationToken);

        var headResponse = new SessionHeadResponse
        {
            SessionId = session.Id.Value,
            LastSequenceNumber = session.CheckpointCount,
            LastTimelineHeadHash = session.HeadEventHash,
            LastReceiptHash = session.HeadReceiptHash
        };

        var response = new SessionHeartbeatResponse
        {
            ServerTime = DateTimeOffset.UtcNow,
            LeaseExpiresAt = session.LeaseExpiresAt ?? DateTimeOffset.UtcNow,
            SessionTrustState = session.HasLeaseGap ? "Degraded" : "Active",
            SessionHead = headResponse
        };

        return Results.Ok(response);
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound($"Unknown assessment session: {id}");
    }
});

app.MapPost("/sessions/{id}/checkpoints", async (string id, CheckpointRequest request, HttpRequest httpRequest,
    IVerifierStorage storage, CancellationToken cancellationToken) =>
{
    var idempotencyKey = httpRequest.Headers["Idempotency-Key"].FirstOrDefault();
    var validationError = request.GetValidationError(id, idempotencyKey);
    if (validationError is not null)
    {
        return Results.BadRequest(validationError);
    }

    try
    {
        var receipt = await storage.AppendCheckpointAsync(new Checkpoint
        {
            SessionId = new AssessmentSessionId(request.SessionId),
            SequenceNumber = request.SequenceNumber,
            TimelineHeadHash = request.TimelineHeadHash,
            IdempotencyKey = idempotencyKey
        }, cancellationToken);
        return Results.Ok(receipt);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(exception.Message);
    }
});

app.MapPost("/packages", async (HttpRequest request, IPackageSubmissionStore packageStore,
    IVerifierStorage storage, IPackageVerifier verifier, IEducationStore educationStore,
    VerifierStorageOptions options, CancellationToken cancellationToken) =>
{
    var idempotencyKey = request.Headers["Idempotency-Key"].FirstOrDefault();

    if (request.HasFormContentType)
    {
        if (request.Form.Files.GetFile("file") is not { } file)
        {
            return Results.BadRequest("A file upload is required.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".owspkg", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest("Only .owspkg files are accepted.");
        }

        if (file.Length > (options.MaxPackageSizeBytes > 0 ? options.MaxPackageSizeBytes : 50 * 1024 * 1024))
        {
            return Results.BadRequest("Uploaded package exceeds maximum size limit.");
        }

        var sessionId = request.Form["sessionId"].FirstOrDefault();
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var tempStream = File.Create(tempFilePath))
            {
                await file.CopyToAsync(tempStream, cancellationToken);
            }

            var hash = await ComputeSha256HashAsync(tempFilePath, cancellationToken);
            var packageFilePath = Path.Combine(options.LocalStoragePath, $"{hash}.owspkg");

            var submissionRequest = new VerifierPackageSubmissionRequest
            {
                SessionId = sessionId,
                IdempotencyKey = idempotencyKey,
                ObjectStorageProvider = "local",
                ObjectBucket = "packages",
                ObjectKey = $"{hash}.owspkg",
                PackageSha256 = hash,
                PackageSizeBytes = file.Length
            };

            VerifierPackageSubmissionResponse submissionResponse;
            try
            {
                submissionResponse = await packageStore.SubmitAsync(submissionRequest, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                if (exception.Message.Contains("idempotency key already exists", StringComparison.OrdinalIgnoreCase) ||
                    exception.Message.Contains("already registered with different metadata",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Conflict(exception.Message);
                }

                return Results.BadRequest(exception.Message);
            }

            Directory.CreateDirectory(options.LocalStoragePath);
            File.Move(tempFilePath, packageFilePath, overwrite: true);

            ReceiptChain? trustedReceiptChain = null;
            SessionHeadResponse? trustedSessionHead = null;
            VerifierSessionRecord? verifierSession = null;

            if (!string.IsNullOrWhiteSpace(submissionResponse.SessionId))
            {
                var assessmentSessionId = new AssessmentSessionId(submissionResponse.SessionId);
                try
                {
                    trustedReceiptChain = await storage.GetReceiptsAsync(assessmentSessionId, cancellationToken);
                    trustedSessionHead = await storage.GetHeadAsync(assessmentSessionId, cancellationToken);
                    verifierSession = await storage.GetSessionAsync(assessmentSessionId, cancellationToken);
                }
                catch (InvalidOperationException)
                {
                    // Session not found
                }
            }

            var educationContext = await ResolveEducationContextAsync(
                submissionRequest.InstitutionId,
                submissionRequest.AssessmentId,
                submissionRequest.StudentUserId,
                educationStore,
                cancellationToken);

            var verifyRequest = new PackageVerificationRequest
            {
                PackagePath = packageFilePath,
                TrustedReceiptChain = trustedReceiptChain,
                TrustedSessionHead = trustedSessionHead,
                SessionLastHeartbeatAt = verifierSession?.LastHeartbeatAt,
                SessionLeaseExpiresAt = verifierSession?.LeaseExpiresAt,
                SessionHasLeaseGap = verifierSession?.HasLeaseGap ?? false,
                SessionMaxLeaseGapSeconds = verifierSession?.MaxLeaseGapSeconds ?? 0,
                SessionFirstLeaseGapAt = verifierSession?.FirstLeaseGapAt,
                EducationContext = educationContext
            };

            var verificationResult = await verifier.VerifyAsync(verifyRequest, cancellationToken);
            var resultJson = JsonSerializer.Serialize(verificationResult);

            await packageStore.UpdateVerificationResultAsync(
                submissionResponse.SubmissionId,
                "Completed",
                verificationResult.TrustStatus.ToString(),
                resultJson,
                cancellationToken);

            return Results.Ok(new
            {
                submissionId = submissionResponse.SubmissionId,
                sessionId = submissionResponse.SessionId,
                packageSha256 = submissionResponse.PackageSha256,
                verificationStatus = "Completed",
                trustStatus = verificationResult.TrustStatus.ToString(),
                verificationResult = verificationResult
            });
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }
    else
    {
        VerifierPackageSubmissionRequest jsonRequest;
        try
        {
            jsonRequest = await request.ReadFromJsonAsync<VerifierPackageSubmissionRequest>(cancellationToken)
                          ?? throw new JsonException("Request body deserialized to null.");
        }
        catch (Exception ex)
        {
            return Results.BadRequest($"Invalid JSON payload: {ex.Message}");
        }

        jsonRequest = jsonRequest with { IdempotencyKey = idempotencyKey };
        var validationError = jsonRequest.GetValidationError();
        if (validationError is not null)
        {
            return Results.BadRequest(validationError);
        }

        try
        {
            var response = await packageStore.SubmitAsync(jsonRequest, cancellationToken);
            return Results.Ok(response);
        }
        catch (NotSupportedException exception)
        {
            return Results.BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            if (exception.Message.Contains("idempotency key already exists", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("already registered with different metadata",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Results.Conflict(exception.Message);
            }

            return Results.BadRequest(exception.Message);
        }
    }
});

app.MapPut("/packages/{id}", async (string id, HttpRequest request, IPackageSubmissionStore packageStore,
    IVerifierStorage storage, IPackageVerifier verifier, IEducationStore educationStore,
    VerifierStorageOptions options, CancellationToken cancellationToken) =>
{
    var submission = await packageStore.GetAsync(id, cancellationToken);
    if (submission is null)
    {
        return Results.NotFound("Unknown package submission.");
    }

    if (!request.HasFormContentType || request.Form.Files.GetFile("file") is not { } file)
    {
        return Results.BadRequest("A file upload is required.");
    }

    var extension = Path.GetExtension(file.FileName);
    if (!string.Equals(extension, ".owspkg", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest("Only .owspkg files are accepted.");
    }

    if (file.Length > (options.MaxPackageSizeBytes > 0 ? options.MaxPackageSizeBytes : 50 * 1024 * 1024))
    {
        return Results.BadRequest("Uploaded package exceeds maximum size limit.");
    }

    var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.tmp");
    try
    {
        using (var tempStream = File.Create(tempFilePath))
        {
            await file.CopyToAsync(tempStream, cancellationToken);
        }

        var hash = await ComputeSha256HashAsync(tempFilePath, cancellationToken);
        if (!string.Equals(hash, submission.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest("Uploaded package hash does not match the registered package SHA-256 metadata.");
        }

        var packageFilePath = Path.Combine(options.LocalStoragePath, $"{hash}.owspkg");
        Directory.CreateDirectory(options.LocalStoragePath);
        File.Move(tempFilePath, packageFilePath, overwrite: true);

        ReceiptChain? trustedReceiptChain = null;
        SessionHeadResponse? trustedSessionHead = null;
        VerifierSessionRecord? verifierSession = null;

        if (!string.IsNullOrWhiteSpace(submission.SessionId))
        {
            var assessmentSessionId = new AssessmentSessionId(submission.SessionId);
            try
            {
                trustedReceiptChain = await storage.GetReceiptsAsync(assessmentSessionId, cancellationToken);
                trustedSessionHead = await storage.GetHeadAsync(assessmentSessionId, cancellationToken);
                verifierSession = await storage.GetSessionAsync(assessmentSessionId, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // Session not found
            }
        }

        var educationContext = await ResolveEducationContextAsync(
            submission.InstitutionId,
            submission.AssessmentId,
            submission.StudentUserId,
            educationStore,
            cancellationToken);

        var verifyRequest = new PackageVerificationRequest
        {
            PackagePath = packageFilePath,
            TrustedReceiptChain = trustedReceiptChain,
            TrustedSessionHead = trustedSessionHead,
            SessionLastHeartbeatAt = verifierSession?.LastHeartbeatAt,
            SessionLeaseExpiresAt = verifierSession?.LeaseExpiresAt,
            SessionHasLeaseGap = verifierSession?.HasLeaseGap ?? false,
            SessionMaxLeaseGapSeconds = verifierSession?.MaxLeaseGapSeconds ?? 0,
            SessionFirstLeaseGapAt = verifierSession?.FirstLeaseGapAt,
            EducationContext = educationContext
        };

        var verificationResult = await verifier.VerifyAsync(verifyRequest, cancellationToken);
        var resultJson = JsonSerializer.Serialize(verificationResult);

        await packageStore.UpdateVerificationResultAsync(
            id,
            "Completed",
            verificationResult.TrustStatus.ToString(),
            resultJson,
            cancellationToken);

        return Results.Ok(new
        {
            submissionId = id,
            verificationStatus = "Completed",
            trustStatus = verificationResult.TrustStatus.ToString(),
            verificationResult = verificationResult
        });
    }
    finally
    {
        if (File.Exists(tempFilePath))
        {
            File.Delete(tempFilePath);
        }
    }
});

app.MapPost("/packages/{id}/verify", async (string id, IPackageSubmissionStore packageStore,
    IVerifierStorage storage, IPackageVerifier verifier, IEducationStore educationStore,
    VerifierStorageOptions options, CancellationToken cancellationToken) =>
{
    var submission = await packageStore.GetAsync(id, cancellationToken);
    if (submission is null)
    {
        return Results.NotFound("Unknown package submission.");
    }

    var packageFilePath = Path.Combine(options.LocalStoragePath, $"{submission.PackageSha256}.owspkg");
    if (!File.Exists(packageFilePath))
    {
        return Results.BadRequest("Package bytes are not available for verification. Please upload the package file.");
    }

    ReceiptChain? trustedReceiptChain = null;
    SessionHeadResponse? trustedSessionHead = null;
    VerifierSessionRecord? verifierSession = null;

    if (!string.IsNullOrWhiteSpace(submission.SessionId))
    {
        var assessmentSessionId = new AssessmentSessionId(submission.SessionId);
        try
        {
            trustedReceiptChain = await storage.GetReceiptsAsync(assessmentSessionId, cancellationToken);
            trustedSessionHead = await storage.GetHeadAsync(assessmentSessionId, cancellationToken);
            verifierSession = await storage.GetSessionAsync(assessmentSessionId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Session not found
        }
    }

    var educationContext = await ResolveEducationContextAsync(
        submission.InstitutionId,
        submission.AssessmentId,
        submission.StudentUserId,
        educationStore,
        cancellationToken);

    var verifyRequest = new PackageVerificationRequest
    {
        PackagePath = packageFilePath,
        TrustedReceiptChain = trustedReceiptChain,
        TrustedSessionHead = trustedSessionHead,
        SessionLastHeartbeatAt = verifierSession?.LastHeartbeatAt,
        SessionLeaseExpiresAt = verifierSession?.LeaseExpiresAt,
        SessionHasLeaseGap = verifierSession?.HasLeaseGap ?? false,
        SessionMaxLeaseGapSeconds = verifierSession?.MaxLeaseGapSeconds ?? 0,
        SessionFirstLeaseGapAt = verifierSession?.FirstLeaseGapAt,
        EducationContext = educationContext
    };

    var verificationResult = await verifier.VerifyAsync(verifyRequest, cancellationToken);
    var resultJson = JsonSerializer.Serialize(verificationResult);

    await packageStore.UpdateVerificationResultAsync(
        id,
        "Completed",
        verificationResult.TrustStatus.ToString(),
        resultJson,
        cancellationToken);

    return Results.Ok(verificationResult);
});

app.MapGet("/packages/{id}", async (string id, IPackageSubmissionStore packageStore,
    CancellationToken cancellationToken) =>
{
    try
    {
        var submission = await packageStore.GetAsync(id, cancellationToken);
        return submission is null ? Results.NotFound("Unknown package submission.") : Results.Ok(submission);
    }
    catch (NotSupportedException exception)
    {
        return Results.BadRequest(exception.Message);
    }
});

app.MapGet("/sessions/{id}/packages", async (string id, IPackageSubmissionStore packageStore,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await packageStore.ListBySessionAsync(id, cancellationToken));
    }
    catch (NotSupportedException exception)
    {
        return Results.BadRequest(exception.Message);
    }
});

app.MapGet("/packages/{id}/verification", async (string id, IPackageSubmissionStore packageStore,
    CancellationToken cancellationToken) =>
{
    var submission = await packageStore.GetAsync(id, cancellationToken);
    if (submission is null)
    {
        return Results.NotFound("Unknown package submission.");
    }

    if (string.IsNullOrWhiteSpace(submission.VerificationResultJson))
    {
        return Results.NotFound("Verification result not found or package has not been verified yet.");
    }

    return Results.Content(submission.VerificationResultJson, "application/json");
});

app.MapGet("/sessions/{id}/receipts",
    async (string id, IVerifierStorage storage, CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await storage.GetReceiptsAsync(new AssessmentSessionId(id), cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return Results.NotFound(exception.Message);
        }
    });

app.MapGet("/sessions/{id}/head", async (string id, IVerifierStorage storage, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await storage.GetHeadAsync(new AssessmentSessionId(id), cancellationToken));
    }
    catch (InvalidOperationException exception)
    {
        return Results.NotFound(exception.Message);
    }
});

// ── Education endpoints ───────────────────────────────────────────────────

// Institutions
app.MapPost("/education/institutions", async (Institution institution, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    await educationStore.CreateInstitutionAsync(institution, cancellationToken);
    return Results.Ok(institution);
});

app.MapGet("/education/institutions/{id}", async (string id, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    var institution = await educationStore.GetInstitutionAsync(new InstitutionId(id), cancellationToken);
    return institution is null ? Results.NotFound($"Institution '{id}' not found.") : Results.Ok(institution);
});

// Courses
app.MapPost("/education/courses", async (Course course, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    await educationStore.CreateCourseAsync(course, cancellationToken);
    return Results.Ok(course);
});

app.MapGet("/education/courses/{id}", async (string id, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    var course = await educationStore.GetCourseAsync(new CourseId(id), cancellationToken);
    return course is null ? Results.NotFound($"Course '{id}' not found.") : Results.Ok(course);
});

// Class groups
app.MapPost("/education/class-groups", async (ClassGroup classGroup, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    await educationStore.CreateClassGroupAsync(classGroup, cancellationToken);
    return Results.Ok(classGroup);
});

app.MapGet("/education/class-groups/{id}", async (string id, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    var classGroup = await educationStore.GetClassGroupAsync(new ClassGroupId(id), cancellationToken);
    return classGroup is null ? Results.NotFound($"Class group '{id}' not found.") : Results.Ok(classGroup);
});

// Course offerings
app.MapPost("/education/course-offerings", async (CourseOffering offering, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    await educationStore.CreateCourseOfferingAsync(offering, cancellationToken);
    return Results.Ok(offering);
});

app.MapGet("/education/course-offerings/{id}", async (string id, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    var offering = await educationStore.GetCourseOfferingAsync(new CourseOfferingId(id), cancellationToken);
    return offering is null ? Results.NotFound($"Course offering '{id}' not found.") : Results.Ok(offering);
});

// Enrollments
app.MapPost("/education/enrollments", async (Enrollment enrollment, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    await educationStore.CreateEnrollmentAsync(enrollment, cancellationToken);
    return Results.Ok(enrollment);
});

app.MapGet("/education/enrollments/user/{userId}", async (string userId, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    var enrollments = await educationStore.GetEnrollmentsForUserAsync(new UserId(userId), cancellationToken);
    return Results.Ok(enrollments);
});

app.MapGet("/education/enrollments/offering/{offeringId}", async (string offeringId,
    IEducationStore educationStore, CancellationToken cancellationToken) =>
{
    var enrollments = await educationStore.GetEnrollmentsForOfferingAsync(
        new CourseOfferingId(offeringId), cancellationToken);
    return Results.Ok(enrollments);
});

// Assessments
app.MapPost("/education/assessments", async (Assessment assessment, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    await educationStore.CreateAssessmentAsync(assessment, cancellationToken);
    return Results.Ok(assessment);
});

app.MapGet("/education/assessments/{id}", async (string id, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    var assessment = await educationStore.GetAssessmentAsync(new AssessmentId(id), cancellationToken);
    return assessment is null ? Results.NotFound($"Assessment '{id}' not found.") : Results.Ok(assessment);
});

// Users / students
app.MapPost("/education/users", async (User user, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    await educationStore.CreateUserAsync(user, cancellationToken);
    return Results.Ok(user);
});

app.MapGet("/education/users/{id}", async (string id, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    var user = await educationStore.GetUserAsync(new UserId(id), cancellationToken);
    return user is null ? Results.NotFound($"User '{id}' not found.") : Results.Ok(user);
});

// ── End education endpoints ───────────────────────────────────────────────

app.Run();

/// <summary>
/// Resolves a <see cref="ReportEducationContext"/> from the education store using the supplied identifiers.
/// Returns <see langword="null"/> when no education identifiers are provided.
/// </summary>
static async Task<ReportEducationContext?> ResolveEducationContextAsync(
    string? institutionId,
    string? assessmentId,
    string? studentUserId,
    IEducationStore educationStore,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(institutionId)
        && string.IsNullOrWhiteSpace(assessmentId)
        && string.IsNullOrWhiteSpace(studentUserId))
    {
        return null;
    }

    Institution? institution = null;
    if (!string.IsNullOrWhiteSpace(institutionId))
    {
        institution = await educationStore.GetInstitutionAsync(new InstitutionId(institutionId), cancellationToken);
    }

    Assessment? assessment = null;
    Course? course = null;
    CourseOffering? offering = null;
    if (!string.IsNullOrWhiteSpace(assessmentId))
    {
        assessment = await educationStore.GetAssessmentAsync(new AssessmentId(assessmentId), cancellationToken);
        if (assessment is not null)
        {
            offering = await educationStore.GetCourseOfferingAsync(assessment.CourseOfferingId, cancellationToken);
            if (offering is not null)
            {
                course = await educationStore.GetCourseAsync(offering.CourseId, cancellationToken);
            }
        }
    }

    User? student = null;
    if (!string.IsNullOrWhiteSpace(studentUserId))
    {
        student = await educationStore.GetUserAsync(new UserId(studentUserId), cancellationToken);
    }

    return new ReportEducationContext
    {
        InstitutionId = institution?.Id.Value,
        InstitutionName = institution?.Name,
        CourseId = course?.Id.Value,
        CourseCode = course?.Code,
        CourseTitle = course?.Title,
        AssessmentId = assessment?.Id.Value,
        AssessmentTitle = assessment?.Title,
        StudentUserId = student?.Id.Value,
        StudentDisplayName = student?.DisplayName,
        StudentExternalId = student?.ExternalId
    };
}

static async Task<string> ComputeSha256HashAsync(string filePath, CancellationToken cancellationToken)
{
    using var stream = File.OpenRead(filePath);
    using var sha256 = SHA256.Create();
    var bytes = await sha256.ComputeHashAsync(stream, cancellationToken);
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

// ponytail: config-backed keys are enough for v0.1; add a real identity provider only when external operators need user-level auth.
static List<VerifierAccessContext> BuildConfiguredApiKeys(VerifierSecurityOptions options)
{
    var configuredKeys = new List<VerifierAccessContext>();
    if (!string.IsNullOrWhiteSpace(options.ApiKey))
    {
        configuredKeys.Add(new VerifierAccessContext(VerifierRolePolicy.Operator, null, options.ApiKey));
    }

    foreach (var apiKey in options.ApiKeys)
    {
        if (string.IsNullOrWhiteSpace(apiKey.Key))
        {
            continue;
        }

        configuredKeys.Add(new VerifierAccessContext(
            NormalizeVerifierRoleName(apiKey.Role),
            apiKey.InstitutionId,
            apiKey.Key));
    }

    return configuredKeys;
}

static string? TryGetSuppliedApiKey(HttpRequest request, string headerName)
{
    if (string.IsNullOrWhiteSpace(headerName) ||
        !request.Headers.TryGetValue(headerName, out var suppliedValues))
    {
        return null;
    }

    var suppliedKey = suppliedValues.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(suppliedKey))
    {
        return null;
    }

    return suppliedKey;
}

// Checks configured bootstrap API keys without leaking timing differences for equal-length values.
static VerifierAccessContext? TryAuthenticateConfiguredApiKey(
    string suppliedKey,
    IReadOnlyList<VerifierAccessContext> configuredApiKeys)
{
    var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
    foreach (var configuredApiKey in configuredApiKeys)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(configuredApiKey.Key);
        if (suppliedBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes))
        {
            return configuredApiKey;
        }
    }

    return null;
}

static async Task<VerifierAccessContext?> TryAuthenticatePersistedApiKeyAsync(
    IVerifierApiKeyStore apiKeyStore,
    string suppliedKey,
    CancellationToken cancellationToken)
{
    try
    {
        return await apiKeyStore.AuthenticateAsync(suppliedKey, cancellationToken);
    }
    catch
    {
        return null;
    }
}

// Reviewer keys are deliberately narrow: read-only verifier evidence queries, scoped to one institution.
static async Task<bool> IsAuthorizedAsync(
    HttpContext context,
    VerifierAccessContext access,
    CancellationToken cancellationToken)
{
    if (IsOperatorRole(access.Role))
    {
        return true;
    }

    if (!HttpMethods.IsGet(context.Request.Method))
    {
        return false;
    }

    var segments = context.Request.Path.Value?
                       .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   ?? [];
    if (segments.Length == 0)
    {
        return false;
    }

    if (segments is ["auth", "api-keys"] or ["auth", "api-keys", _, "revoke"])
    {
        return IsOperatorRole(access.Role);
    }

    if (segments is ["packages", _] or ["packages", _, "verification"])
    {
        var packageStore = context.RequestServices.GetRequiredService<IPackageSubmissionStore>();
        var submission = await packageStore.GetAsync(segments[1], cancellationToken);
        return submission is not null && await MatchesInstitutionScopeAsync(
            submission.InstitutionId,
            submission.SessionId,
            access,
            context.RequestServices.GetRequiredService<IVerifierStorage>(),
            cancellationToken);
    }

    if (segments is ["sessions", _, "packages"] or ["sessions", _, "receipts"] or ["sessions", _, "head"])
    {
        return await MatchesInstitutionScopeAsync(
            null,
            segments[1],
            access,
            context.RequestServices.GetRequiredService<IVerifierStorage>(),
            cancellationToken);
    }

    if (segments.Length >= 2 && string.Equals(segments[0], "education", StringComparison.OrdinalIgnoreCase))
    {
        var educationStore = context.RequestServices.GetRequiredService<IEducationStore>();
        var institutionId = await ResolveEducationInstitutionIdAsync(segments, educationStore, cancellationToken);
        return !string.IsNullOrWhiteSpace(institutionId) &&
               string.Equals(institutionId, access.InstitutionId, StringComparison.OrdinalIgnoreCase);
    }

    return false;
}

// Session metadata already carries institution scope, so reuse it instead of adding a second auth store.
static async Task<bool> MatchesInstitutionScopeAsync(
    string? institutionId,
    string? sessionId,
    VerifierAccessContext access,
    IVerifierStorage storage,
    CancellationToken cancellationToken)
{
    var resolvedInstitutionId = NormalizeInstitutionIdValue(institutionId);
    if (string.IsNullOrWhiteSpace(resolvedInstitutionId) && !string.IsNullOrWhiteSpace(sessionId))
    {
        try
        {
            var session = await storage.GetSessionAsync(new AssessmentSessionId(sessionId), cancellationToken);
            resolvedInstitutionId = NormalizeInstitutionIdValue(TryGetInstitutionIdFromMetadata(session.MetadataJson));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    return !string.IsNullOrWhiteSpace(resolvedInstitutionId) &&
           string.Equals(resolvedInstitutionId, access.InstitutionId, StringComparison.OrdinalIgnoreCase);
}

// Session metadata is already a tiny JSON blob; pull only the institution id we need for reviewer scoping.
static string? TryGetInstitutionIdFromMetadata(string? metadataJson)
{
    if (string.IsNullOrWhiteSpace(metadataJson))
    {
        return null;
    }

    try
    {
        using var document = JsonDocument.Parse(metadataJson);
        return document.RootElement.TryGetProperty("institutionId", out var institutionIdElement)
            ? institutionIdElement.GetString()
            : null;
    }
    catch (JsonException)
    {
        return null;
    }
}

static bool IsSupportedVerifierRole(string role) =>
    VerifierRolePolicy.IsSupportedRole(role);

static bool IsOperatorRole(string role) =>
    VerifierRolePolicy.IsOperatorRole(role);

static bool IsInstructorReviewerRole(string role) =>
    VerifierRolePolicy.IsInstructorReviewerRole(role);

static string NormalizeVerifierRoleName(string role) =>
    VerifierRolePolicy.NormalizeRoleName(role);

static string? NormalizeInstitutionIdValue(string? institutionId) =>
    VerifierRolePolicy.NormalizeInstitutionId(institutionId);

static async Task<string?> ResolveEducationInstitutionIdAsync(
    string[] segments,
    IEducationStore educationStore,
    CancellationToken cancellationToken)
{
    if (segments is ["education", "institutions", var institutionIdSegment])
    {
        return institutionIdSegment;
    }

    if (segments is ["education", "courses", var courseId])
    {
        return (await educationStore.GetCourseAsync(new CourseId(courseId), cancellationToken))?.InstitutionId.Value;
    }

    if (segments is ["education", "class-groups", var classGroupId])
    {
        return (await educationStore.GetClassGroupAsync(new ClassGroupId(classGroupId), cancellationToken))
            ?.InstitutionId.Value;
    }

    if (segments is ["education", "course-offerings", var offeringId])
    {
        return (await educationStore.GetCourseOfferingAsync(new CourseOfferingId(offeringId), cancellationToken))
            ?.InstitutionId.Value;
    }

    if (segments is ["education", "assessments", var assessmentId])
    {
        return (await educationStore.GetAssessmentAsync(new AssessmentId(assessmentId), cancellationToken))
            ?.InstitutionId.Value;
    }

    if (segments is ["education", "users", var userId])
    {
        return (await educationStore.GetUserAsync(new UserId(userId), cancellationToken))?.InstitutionId.Value;
    }

    if (segments is ["education", "enrollments", "user", var enrollmentUserId])
    {
        return (await educationStore.GetUserAsync(new UserId(enrollmentUserId), cancellationToken))?.InstitutionId
            .Value;
    }

    if (segments is ["education", "enrollments", "offering", var enrollmentOfferingId])
    {
        return (await educationStore.GetCourseOfferingAsync(new CourseOfferingId(enrollmentOfferingId),
            cancellationToken))?.InstitutionId.Value;
    }

    return null;
}

static bool IsWeakSecret(string secret)
{
    if (string.IsNullOrWhiteSpace(secret)) return true;
    var normalized = secret.Trim().ToLowerInvariant();
    if (normalized.Length < 16) return true;

    string[] unsafeDefaults =
        ["dev-key", "change-me", "change_me", "default", "placeholder", "development", "ows-dev", "ows_dev"];
    return unsafeDefaults.Contains(normalized);
}

public enum VerifierEnvironmentMode
{
    Development,
    Local,
    Production
}

/// <summary>
/// Represents the optional education context that may be supplied when starting a new verifier session.
/// </summary>
public sealed record StartSessionRequest
{
    /// <summary>Gets the optional institution identifier.</summary>
    public string? InstitutionId { get; init; }

    /// <summary>Gets the optional course offering identifier.</summary>
    public string? CourseOfferingId { get; init; }

    /// <summary>Gets the optional assessment identifier.</summary>
    public string? AssessmentId { get; init; }

    /// <summary>Gets the optional student user identifier.</summary>
    public string? StudentUserId { get; init; }
}

sealed record VerifierAccessContext
{
    public VerifierAccessContext(string role, string? institutionId, string key)
    {
        Role = string.IsNullOrWhiteSpace(role) ? "operator" : role.Trim().ToLowerInvariant();
        InstitutionId = string.IsNullOrWhiteSpace(institutionId) ? null : institutionId.Trim();
        Key = key;
    }

    public string Role { get; init; }

    public string? InstitutionId { get; init; }

    public string Key { get; init; }
}

public partial class Program
{
}