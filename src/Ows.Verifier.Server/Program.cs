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

// Check API Key
if (string.IsNullOrWhiteSpace(securityOptions.ApiKey))
{
    if (isProduction)
    {
        startupErrors.Add("VerifierSecurity:ApiKey must be configured in Production mode.");
    }
    else
    {
        startupWarnings.Add("VerifierSecurity:ApiKey is not configured. Request guard is disabled.");
    }
}
else if (IsWeakSecret(securityOptions.ApiKey))
{
    if (isProduction)
    {
        startupErrors.Add("VerifierSecurity:ApiKey is too weak/short for Production mode. It must be at least 16 characters long and not a known default.");
    }
    else
    {
        startupWarnings.Add("VerifierSecurity:ApiKey is weak or using a dev default.");
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
        startupErrors.Add("VerifierStorage:ReceiptSigningKey is too weak/short for Production mode. It must be at least 16 characters long and not a known default.");
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
builder.Services.AddSingleton<IPackageSubmissionStore>(_ => string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
    ? new PostgresPackageSubmissionStore(normalizedStorageOptions.PostgresConnectionString)
    : new JsonFilePackageSubmissionStore(Path.Combine(Path.GetDirectoryName(normalizedStorageOptions.JsonStorePath) ?? builder.Environment.ContentRootPath, "package_submissions.json")));
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
startupLogger.LogInformation("API Guard: {ApiGuardStatus}", string.IsNullOrWhiteSpace(securityOptions.ApiKey) ? "Disabled" : "Enabled");
if (!string.IsNullOrWhiteSpace(securityOptions.ApiKey))
{
    startupLogger.LogInformation("API Header Name: {HeaderName}", securityOptions.HeaderName);
}
var keyFingerprint = ReceiptChainVerifier.ComputeFingerprint(normalizedStorageOptions.ReceiptSigningKey);
startupLogger.LogInformation("Signing Key Fingerprint: {Fingerprint}", string.IsNullOrWhiteSpace(keyFingerprint) ? "None (Unsigned)" : keyFingerprint);

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

if (!string.IsNullOrWhiteSpace(securityOptions.ApiKey))
{
    app.Use(async (context, next) =>
    {
        if (!HasValidApiKey(context.Request, securityOptions))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            var hasHeader = context.Request.Headers.ContainsKey(securityOptions.HeaderName);
            var message = hasHeader ? "Invalid verifier API key." : "Verifier API key is required.";
            await context.Response.WriteAsync(message);
            return;
        }

        await next(context);
    });
}

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.MapGet("/ready", async (IVerifierStorage storage, VerifierStorageOptions options, CancellationToken cancellationToken) =>
{
    try
    {
        var healthy = await storage.CheckHealthAsync(cancellationToken);
        if (!healthy)
        {
            return Results.Json(new { status = "Unhealthy", error = "Storage health check failed." }, statusCode: StatusCodes.Status503ServiceUnavailable);
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
        return Results.Json(new { status = "Unhealthy", error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

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
                    exception.Message.Contains("already registered with different metadata", StringComparison.OrdinalIgnoreCase))
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
                exception.Message.Contains("already registered with different metadata", StringComparison.OrdinalIgnoreCase))
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

// Checks the optional shared verifier API key without leaking timing differences for equal-length values.
static bool HasValidApiKey(HttpRequest request, VerifierSecurityOptions options)
{
    if (string.IsNullOrWhiteSpace(options.HeaderName) ||
        !request.Headers.TryGetValue(options.HeaderName, out var suppliedValues))
    {
        return false;
    }

    var suppliedKey = suppliedValues.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(suppliedKey))
    {
        return false;
    }

    var expectedBytes = Encoding.UTF8.GetBytes(options.ApiKey);
    var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
    return suppliedBytes.Length == expectedBytes.Length &&
           CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
}

static bool IsWeakSecret(string secret)
{
    if (string.IsNullOrWhiteSpace(secret)) return true;
    var normalized = secret.Trim().ToLowerInvariant();
    if (normalized.Length < 16) return true;
    
    string[] unsafeDefaults = ["dev-key", "change-me", "change_me", "default", "placeholder", "development", "ows-dev", "ows_dev"];
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

public partial class Program { }
