using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Ows.Core.Notarization;
using Ows.Verifier.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
var storageOptions = builder.Configuration.GetSection("VerifierStorage").Get<VerifierStorageOptions>()
                     ?? new VerifierStorageOptions();
var securityOptions = builder.Configuration.GetSection("VerifierSecurity").Get<VerifierSecurityOptions>()
                      ?? new VerifierSecurityOptions();
var normalizedStorageOptions = string.IsNullOrWhiteSpace(storageOptions.JsonStorePath)
    ? storageOptions with
    {
        JsonStorePath = Path.Combine(builder.Environment.ContentRootPath, ".ows-verifier", "receipts.json")
    }
    : storageOptions;

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
builder.Services.AddSingleton(_ => string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
    ? new PostgresPackageSubmissionStore(normalizedStorageOptions.PostgresConnectionString)
    : new PostgresPackageSubmissionStore());
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

app.MapPost("/sessions", async (IVerifierStorage storage, CancellationToken cancellationToken) =>
{
    var session = await storage.CreateSessionAsync(cancellationToken);
    return Results.Ok(new StartSessionResponse { SessionId = session.Id.Value });
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

app.MapPost("/packages", async (VerifierPackageSubmissionRequest request, HttpRequest httpRequest,
    PostgresPackageSubmissionStore packageStore, CancellationToken cancellationToken) =>
{
    request = request with { IdempotencyKey = httpRequest.Headers["Idempotency-Key"].FirstOrDefault() };
    var validationError = request.GetValidationError();
    if (validationError is not null)
    {
        return Results.BadRequest(validationError);
    }

    try
    {
        return Results.Ok(await packageStore.SubmitAsync(request, cancellationToken));
    }
    catch (NotSupportedException exception)
    {
        return Results.BadRequest(exception.Message);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(exception.Message);
    }
});

app.MapGet("/packages/{id}", async (string id, PostgresPackageSubmissionStore packageStore,
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

app.Run();

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

public partial class Program { }
