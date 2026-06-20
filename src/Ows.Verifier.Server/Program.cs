using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Ows.Core.Education;
using Ows.Core.Notarization;
using Ows.Core.Reporting;
using Ows.Core.Verification;
using Ows.Verifier.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
var storageOptions = builder.Configuration.GetSection("VerifierStorage").Get<VerifierStorageOptions>()
                     ?? new VerifierStorageOptions();
var packageWorkerEnabled = ResolvePackageWorkerEnabled(builder.Configuration, storageOptions.PackageWorkerEnabled);
var applyMigrationsOnStartup = ResolveApplyMigrationsOnStartup(builder.Configuration, storageOptions.ApplyMigrationsOnStartup);
var securityOptions = builder.Configuration.GetSection("VerifierSecurity").Get<VerifierSecurityOptions>()
                      ?? new VerifierSecurityOptions();
var normalizedStorageOptions = storageOptions with
{
    PackageWorkerEnabled = packageWorkerEnabled,
    ApplyMigrationsOnStartup = applyMigrationsOnStartup,
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

if (string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase) &&
    normalizedStorageOptions.ApplyMigrationsOnStartup)
{
    startupWarnings.Add(
        "VerifierStorage:ApplyMigrationsOnStartup is enabled. Multi-instance deployments should run 'migrate' once, then set it to false on steady-state instances.");
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
builder.Services.AddSingleton<IReportGenerator, OwsReportGenerator>();
builder.Services.AddSingleton<IVerifierApiKeyStore>(_ =>
{
    var storeRoot = Path.GetDirectoryName(normalizedStorageOptions.JsonStorePath) ??
                    builder.Environment.ContentRootPath;
    return string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
        ? new PostgresVerifierApiKeyStore(
            !string.IsNullOrWhiteSpace(normalizedStorageOptions.PostgresConnectionString)
                ? normalizedStorageOptions.PostgresConnectionString
                : throw new InvalidOperationException(
                    "VerifierStorage:PostgresConnectionString must be configured when VerifierStorage:Provider=postgres."),
            normalizedStorageOptions.ApplyMigrationsOnStartup)
        : new JsonFileVerifierApiKeyStore(Path.Combine(storeRoot, "api_keys.json"));
});
builder.Services.AddSingleton<IPackageSubmissionStore>(_ =>
    string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
        ? new PostgresPackageSubmissionStore(
            normalizedStorageOptions.PostgresConnectionString,
            normalizedStorageOptions.ApplyMigrationsOnStartup)
        : new JsonFilePackageSubmissionStore(Path.Combine(
            Path.GetDirectoryName(normalizedStorageOptions.JsonStorePath) ?? builder.Environment.ContentRootPath,
            "package_submissions.json")));
builder.Services.AddSingleton<IPackageBlobStore>(_ =>
    new LocalFilePackageBlobStore(normalizedStorageOptions.LocalStoragePath, normalizedStorageOptions.MaxPackageSizeBytes));
builder.Services.AddSingleton<IPackageVerificationJobStore>(_ =>
{
    var storeRoot = Path.GetDirectoryName(normalizedStorageOptions.JsonStorePath) ??
                    builder.Environment.ContentRootPath;
    return string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
        ? new PostgresPackageVerificationJobStore(
            !string.IsNullOrWhiteSpace(normalizedStorageOptions.PostgresConnectionString)
                ? normalizedStorageOptions.PostgresConnectionString
                : throw new InvalidOperationException(
                    "VerifierStorage:PostgresConnectionString must be configured when VerifierStorage:Provider=postgres."),
            normalizedStorageOptions.ApplyMigrationsOnStartup)
        : new JsonFilePackageVerificationJobStore(Path.Combine(storeRoot, "package_verification_jobs.json"));
});
if (normalizedStorageOptions.PackageWorkerEnabled)
{
    builder.Services.AddHostedService<PackageVerificationWorker>();
}
builder.Services.AddSingleton<IVerifierAuditStore>(_ =>
{
    var storeRoot = Path.GetDirectoryName(normalizedStorageOptions.JsonStorePath) ??
                    builder.Environment.ContentRootPath;
    return string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
        ? new PostgresVerifierAuditStore(
            !string.IsNullOrWhiteSpace(normalizedStorageOptions.PostgresConnectionString)
                ? normalizedStorageOptions.PostgresConnectionString
                : throw new InvalidOperationException(
                    "VerifierStorage:PostgresConnectionString must be configured when VerifierStorage:Provider=postgres."),
            normalizedStorageOptions.ApplyMigrationsOnStartup)
        : new JsonFileVerifierAuditStore(Path.Combine(storeRoot, "audit_events.json"));
});
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
        normalizedStorageOptions.ReceiptSigningKey,
        normalizedStorageOptions.ApplyMigrationsOnStartup),
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
var auditLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit");

startupLogger.LogInformation("OWS Verifier starting up...");
startupLogger.LogInformation("Environment Mode: {EnvironmentMode}", envMode);
startupLogger.LogInformation("Storage Provider: {Provider}", normalizedStorageOptions.Provider);
startupLogger.LogInformation("Instance Mode: {InstanceMode}", DescribeInstanceMode(normalizedStorageOptions.PackageWorkerEnabled));
startupLogger.LogInformation("Package Verification Worker Enabled: {WorkerEnabled}", normalizedStorageOptions.PackageWorkerEnabled);
startupLogger.LogInformation("Apply Migrations On Startup: {ApplyMigrationsOnStartup}", normalizedStorageOptions.ApplyMigrationsOnStartup);
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
        startupLogger.LogInformation(
            "Verifier storage initialized successfully ({InitializationMode}).",
            normalizedStorageOptions.ApplyMigrationsOnStartup ? "database/migrations ready" : "database access ready");
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

var auditStore = app.Services.GetRequiredService<IVerifierAuditStore>();
try
{
    startupLogger.LogInformation("Initializing verifier audit store...");
    await auditStore.InitializeAsync(CancellationToken.None);
    startupLogger.LogInformation("Verifier audit store initialized successfully.");
}
catch (Exception ex)
{
    startupLogger.LogError(ex, "Failed to initialize verifier audit store.");
    if (isProduction)
    {
        throw;
    }
}

var packageJobStore = app.Services.GetRequiredService<IPackageVerificationJobStore>();
try
{
    startupLogger.LogInformation("Initializing package verification job store...");
    await packageJobStore.InitializeAsync(CancellationToken.None);
    startupLogger.LogInformation("Package verification job store initialized successfully.");
}
catch (Exception ex)
{
    startupLogger.LogError(ex, "Failed to initialize package verification job store.");
    if (isProduction)
    {
        throw;
    }
}

app.Use(async (context, next) =>
{
    var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(requestId))
    {
        requestId = Guid.NewGuid().ToString("N");
    }

    context.Items["RequestId"] = requestId;
    context.Response.Headers["X-Request-Id"] = requestId;
    await next(context);
});

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
            "Verifier request {RequestId} {Method} {Path} returned {StatusCode} in {ElapsedMilliseconds} ms. role={Role} institutionId={InstitutionId} keyPrefix={KeyPrefix}",
            GetRequestId(context),
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            TryGetAccessContext(context)?.Role ?? "Anonymous",
            TryGetAccessContext(context)?.InstitutionId,
            TryGetAccessContext(context)?.KeyPrefix);
    }
});

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    var isBypassPath = HttpMethods.IsGet(context.Request.Method) &&
        (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(path, "/ready", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(path, "/metrics", StringComparison.OrdinalIgnoreCase));
    if (isBypassPath)
    {
        await next(context);
        return;
    }

    var auditStore = context.RequestServices.GetRequiredService<IVerifierAuditStore>();
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
        await WriteAuditEventAsync(
            auditStore,
            auditLogger,
            context,
            eventType: "auth.failed",
            result: hasHeader ? "InvalidKey" : "MissingKey",
            metadata: CreateMetadata(
                ("endpoint", context.Request.Path.Value),
                ("method", context.Request.Method)),
            actorKeyPrefix: hasHeader ? CreateSafeKeyPrefix(suppliedKey) : null,
            cancellationToken: context.RequestAborted);
        await context.Response.WriteAsync(message);
        return;
    }

    if (!await IsAuthorizedAsync(context, access, context.RequestAborted))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await WriteAuditEventAsync(
            auditStore,
            auditLogger,
            context,
            eventType: "access.denied",
            result: "Forbidden",
            access: access,
            institutionId: access.InstitutionId,
            sessionId: TryGetRouteValue(context, "id"),
            packageId: TryGetPackageRouteId(context),
            metadata: CreateMetadata(
                ("endpoint", context.Request.Path.Value),
                ("method", context.Request.Method)),
            cancellationToken: context.RequestAborted);
        await context.Response.WriteAsync("Verifier API key is not authorized for this resource.");
        return;
    }

    context.Items["VerifierAccessContext"] = access;
    await next(context);
});

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.MapGet("/metrics",
    async (HttpContext context, IVerifierAuditStore auditStore, IPackageVerificationJobStore jobStore,
        IVerifierStorage storage, IEducationStore educationStore, IPackageBlobStore blobStore,
        VerifierStorageOptions options, CancellationToken cancellationToken) =>
    {
        var summary = await auditStore.GetSummaryAsync(cancellationToken);
        var jobSummary = await jobStore.GetSummaryAsync(cancellationToken);

        bool storageReady = false;
        try { storageReady = await storage.CheckHealthAsync(cancellationToken); } catch { }

        bool educationReady = false;
        try { educationReady = await CheckEducationStoreReadyAsync(educationStore, cancellationToken); } catch { }

        bool packageStorageReady = false;
        try { packageStorageReady = await blobStore.CheckHealthAsync(cancellationToken); } catch { }

        var signingConfigured = !string.IsNullOrWhiteSpace(options.ReceiptSigningKey);

        var auditTotalSum = summary.SessionsCreated +
                             summary.CheckpointsAccepted +
                             summary.HeartbeatsAccepted +
                             summary.PackagesSubmitted +
                             summary.PackagesVerified +
                             summary.ReportsRead +
                             summary.AuthFailures +
                             summary.AccessDenied;

        var sb = new StringBuilder();
        sb.AppendLine("# HELP ows_sessions_created_total Total number of OWS assessment sessions created.");
        sb.AppendLine("# TYPE ows_sessions_created_total counter");
        sb.AppendLine($"ows_sessions_created_total {summary.SessionsCreated}");
        sb.AppendLine();

        sb.AppendLine("# HELP ows_checkpoints_accepted_total Total number of checkpoints accepted.");
        sb.AppendLine("# TYPE ows_checkpoints_accepted_total counter");
        sb.AppendLine($"ows_checkpoints_accepted_total {summary.CheckpointsAccepted}");
        sb.AppendLine();

        sb.AppendLine("# HELP ows_heartbeats_accepted_total Total number of heartbeats accepted.");
        sb.AppendLine("# TYPE ows_heartbeats_accepted_total counter");
        sb.AppendLine($"ows_heartbeats_accepted_total {summary.HeartbeatsAccepted}");
        sb.AppendLine();

        sb.AppendLine("# HELP ows_package_uploads_total Total number of package uploads.");
        sb.AppendLine("# TYPE ows_package_uploads_total counter");
        sb.AppendLine($"ows_package_uploads_total {summary.PackagesSubmitted}");
        sb.AppendLine();

        sb.AppendLine("# HELP ows_package_verification_jobs_total Total number of package verification jobs by status.");
        sb.AppendLine("# TYPE ows_package_verification_jobs_total gauge");
        sb.AppendLine($"ows_package_verification_jobs_total{{status=\"pending\"}} {jobSummary.Pending}");
        sb.AppendLine($"ows_package_verification_jobs_total{{status=\"running\"}} {jobSummary.Running}");
        sb.AppendLine($"ows_package_verification_jobs_total{{status=\"succeeded\"}} {jobSummary.Succeeded}");
        sb.AppendLine($"ows_package_verification_jobs_total{{status=\"failed\"}} {jobSummary.Failed}");
        sb.AppendLine();

        sb.AppendLine("# HELP ows_auth_failures_total Total number of API key authentication failures.");
        sb.AppendLine("# TYPE ows_auth_failures_total counter");
        sb.AppendLine($"ows_auth_failures_total {summary.AuthFailures}");
        sb.AppendLine();

        sb.AppendLine("# HELP ows_access_denied_total Total number of forbidden access attempts.");
        sb.AppendLine("# TYPE ows_access_denied_total counter");
        sb.AppendLine($"ows_access_denied_total {summary.AccessDenied}");
        sb.AppendLine();

        sb.AppendLine("# HELP ows_audit_events_total Sum of all recorded audit events.");
        sb.AppendLine("# TYPE ows_audit_events_total counter");
        sb.AppendLine($"ows_audit_events_total {auditTotalSum}");
        sb.AppendLine();

        sb.AppendLine("# HELP ows_ready_dependency_status Health readiness status of OWS verifier dependencies (1 = healthy, 0 = unhealthy).");
        sb.AppendLine("# TYPE ows_ready_dependency_status gauge");
        sb.AppendLine($"ows_ready_dependency_status{{dependency=\"storage\"}} {(storageReady ? 1 : 0)}");
        sb.AppendLine($"ows_ready_dependency_status{{dependency=\"education\"}} {(educationReady ? 1 : 0)}");
        sb.AppendLine($"ows_ready_dependency_status{{dependency=\"packages\"}} {(packageStorageReady ? 1 : 0)}");
        sb.AppendLine($"ows_ready_dependency_status{{dependency=\"signing\"}} {(signingConfigured ? 1 : 0)}");

        return Results.Content(sb.ToString(), "text/plain; version=0.0.4; charset=utf-8");
    });

app.MapGet("/ready",
    async (HttpContext context, IVerifierStorage storage, IEducationStore educationStore, IVerifierApiKeyStore apiKeyStore,
        IPackageBlobStore blobStore, VerifierStorageOptions options, CancellationToken cancellationToken) =>
    {
        var signingConfigured = !string.IsNullOrWhiteSpace(options.ReceiptSigningKey);
        var packageStorageConfigured = !string.IsNullOrWhiteSpace(options.LocalStoragePath);
        var packageStorageProvider = DescribePackageStorageProvider(options);
        var instanceMode = DescribeInstanceMode(options.PackageWorkerEnabled);
        try
        {
            var authMode = await ResolveAuthModeAsync(apiKeyStore, configuredApiKeys.Count > 0, cancellationToken);
            var healthy = await storage.CheckHealthAsync(cancellationToken);
            var educationReady = await CheckEducationStoreReadyAsync(educationStore, cancellationToken);
            var packageStorageReady = await blobStore.CheckHealthAsync(cancellationToken);
            var warnings = BuildDeploymentWarnings(options, packageStorageReady);
            if (!healthy || !educationReady || !packageStorageReady)
            {
                await WriteAuditEventAsync(
                    app.Services.GetRequiredService<IVerifierAuditStore>(),
                    auditLogger,
                    context,
                    eventType: "readiness.failed",
                    result: "Unhealthy",
                    metadata: CreateMetadata(
                        ("storageProvider", options.Provider),
                        ("storageReady", healthy.ToString()),
                        ("educationStoreReady", educationReady.ToString()),
                        ("packageStorageReady", packageStorageReady.ToString()),
                        ("workerEnabled", options.PackageWorkerEnabled.ToString()),
                        ("instanceMode", instanceMode),
                        ("signingConfigured", signingConfigured.ToString()),
                        ("authMode", authMode)),
                    cancellationToken: cancellationToken);
                return Results.Json(new
                {
                    status = "Unhealthy",
                    storage = options.Provider,
                    storageProvider = options.Provider,
                    packageStorageProvider,
                    packageStorageConfigured,
                    workerEnabled = options.PackageWorkerEnabled,
                    instanceMode,
                    applyMigrationsOnStartup = options.ApplyMigrationsOnStartup,
                    signing = signingConfigured ? "Enabled" : "Disabled",
                    warnings,
                    dependencies = new
                    {
                        storageProvider = options.Provider,
                        storageReady = healthy,
                        educationStoreReady = educationReady,
                        packageStorageConfigured,
                        packageStorageReady,
                        workerEnabled = options.PackageWorkerEnabled,
                        instanceMode,
                        signingConfigured,
                        authMode
                    }
                }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new
            {
                status = "Ready",
                storage = options.Provider,
                storageProvider = options.Provider,
                packageStorageProvider,
                packageStorageConfigured,
                workerEnabled = options.PackageWorkerEnabled,
                instanceMode,
                applyMigrationsOnStartup = options.ApplyMigrationsOnStartup,
                signing = signingConfigured ? "Enabled" : "Disabled",
                warnings,
                dependencies = new
                {
                    storageProvider = options.Provider,
                    storageReady = true,
                    educationStoreReady = true,
                    packageStorageConfigured,
                    packageStorageReady = true,
                    workerEnabled = options.PackageWorkerEnabled,
                    instanceMode,
                    signingConfigured,
                    authMode
                }
            });
        }
        catch (Exception exception)
        {
            var authMode = "unknown";
            var packageStorageReady = false;
            try
            {
                authMode = await ResolveAuthModeAsync(apiKeyStore, configuredApiKeys.Count > 0, cancellationToken);
                packageStorageReady = await blobStore.CheckHealthAsync(cancellationToken);
            }
            catch
            {
                // Keep the readiness response secret-safe even when auth storage is unavailable.
            }

            await WriteAuditEventAsync(
                app.Services.GetRequiredService<IVerifierAuditStore>(),
                auditLogger,
                context,
                eventType: "readiness.failed",
                result: "Exception",
                metadata: CreateMetadata(
                    ("storageProvider", options.Provider),
                    ("packageStorageReady", packageStorageReady.ToString()),
                    ("signingConfigured", signingConfigured.ToString()),
                    ("authMode", authMode),
                    ("exceptionType", exception.GetType().Name)),
                cancellationToken: cancellationToken);
            return Results.Json(new
            {
                status = "Unhealthy",
                storage = options.Provider,
                storageProvider = options.Provider,
                packageStorageProvider,
                packageStorageConfigured,
                workerEnabled = options.PackageWorkerEnabled,
                instanceMode,
                applyMigrationsOnStartup = options.ApplyMigrationsOnStartup,
                signing = signingConfigured ? "Enabled" : "Disabled",
                warnings = BuildDeploymentWarnings(options, packageStorageReady),
                dependencies = new
                {
                    storageProvider = options.Provider,
                    storageReady = false,
                    educationStoreReady = false,
                    packageStorageConfigured,
                    packageStorageReady,
                    workerEnabled = options.PackageWorkerEnabled,
                    instanceMode,
                    signingConfigured,
                    authMode
                }
            },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    });

app.MapPost("/auth/api-keys", async (HttpContext context, VerifierApiKeyCreateRequest request,
    IVerifierApiKeyStore apiKeyStore, IVerifierAuditStore auditStore, CancellationToken cancellationToken) =>
{
    // Delegation policy:
    //   Operator → can create any role.
    //   InstitutionAdmin → can create InstructorReviewer for own institution only.
    //   InstructorReviewer → cannot create keys.
    var callerAccess = TryGetAccessContext(context);
    if (!IsOperatorRole(callerAccess?.Role ?? string.Empty))
    {
        if (!IsInstitutionAdminRole(callerAccess?.Role ?? string.Empty))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        // InstitutionAdmin can only create InstructorReviewer or StudentClient keys for their own institution.
        var targetRole = NormalizeVerifierRoleName(request.Role);
        var isAllowedTargetRole = string.Equals(targetRole, VerifierRolePolicy.InstructorReviewer, StringComparison.Ordinal) ||
                                  string.Equals(targetRole, VerifierRolePolicy.StudentClient, StringComparison.Ordinal);
        if (!isAllowedTargetRole)
        {
            return Results.Json(
                new { error = "InstitutionAdmin may only create InstructorReviewer or StudentClient keys." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!string.Equals(request.InstitutionId, callerAccess!.InstitutionId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(
                new { error = "InstitutionAdmin may only create keys for their own institution." },
                statusCode: StatusCodes.Status403Forbidden);
        }
    }

    try
    {
        var result = await apiKeyStore.CreateAsync(request, cancellationToken);
        await WriteAuditEventAsync(
            auditStore,
            auditLogger,
            context,
            eventType: "api_key.created",
            result: "Created",
            access: callerAccess,
            institutionId: result.Metadata.InstitutionId,
            metadata: CreateMetadata(
                ("createdKeyId", result.Metadata.KeyId),
                ("createdKeyPrefix", result.Metadata.KeyPrefix),
                ("createdRole", result.Metadata.Role),
                ("expiresAtUtc", result.Metadata.ExpiresAtUtc?.ToString("o"))),
            cancellationToken: cancellationToken);
        return Results.Ok(result);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(exception.Message);
    }
});

app.MapGet("/auth/api-keys", async (IVerifierApiKeyStore apiKeyStore, CancellationToken cancellationToken) =>
    Results.Ok(await apiKeyStore.ListAsync(cancellationToken)));

app.MapPost("/auth/api-keys/{id}/revoke", async (HttpContext context, string id, IVerifierApiKeyStore apiKeyStore,
    IVerifierAuditStore auditStore, CancellationToken cancellationToken) =>
{
    var revoked = await apiKeyStore.RevokeAsync(id, cancellationToken);
    if (!revoked)
    {
        return Results.NotFound("Unknown verifier API key.");
    }

    await WriteAuditEventAsync(
        auditStore,
        auditLogger,
        context,
        eventType: "api_key.revoked",
        result: "Revoked",
        access: TryGetAccessContext(context),
        metadata: CreateMetadata(("revokedKeyId", id)),
        cancellationToken: cancellationToken);
    return Results.Ok(new { keyId = id, revoked = true });
});

app.MapPost("/sessions", async (HttpContext context, StartSessionRequest? body, IVerifierStorage storage,
    IEducationStore educationStore, IVerifierAuditStore auditStore, CancellationToken cancellationToken) =>
{
    var callerAccess = TryGetAccessContext(context);
    if (callerAccess is not null && !IsOperatorRole(callerAccess.Role))
    {
        if (string.IsNullOrWhiteSpace(body?.InstitutionId) ||
            !string.Equals(body.InstitutionId, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(
                new { error = $"{callerAccess.Role} may only create sessions for their own institution." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (IsStudentClientRole(callerAccess.Role) && !string.IsNullOrWhiteSpace(callerAccess.StudentUserId))
        {
            if (string.IsNullOrWhiteSpace(body?.StudentUserId) ||
                !string.Equals(body.StudentUserId, callerAccess.StudentUserId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    new { error = "StudentClient bound key must match the student user ID in the session request." },
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }
    }

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
    await WriteAuditEventAsync(
        auditStore,
        auditLogger,
        context,
        eventType: "session.created",
        result: "Created",
        access: TryGetAccessContext(context),
        institutionId: body?.InstitutionId,
        sessionId: session.Id.Value,
        assessmentId: assessmentId,
        metadata: CreateMetadata(("clientId", clientId)),
        cancellationToken: cancellationToken);
    return Results.Ok(new StartSessionResponse { SessionId = session.Id.Value });
});

app.MapPost("/sessions/{id}/heartbeat", async (string id, SessionHeartbeatRequest request,
    HttpContext context, IVerifierStorage storage, IVerifierAuditStore auditStore, CancellationToken cancellationToken) =>
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

        var institutionId = NormalizeInstitutionIdValue(TryGetInstitutionIdFromMetadata(session.MetadataJson));
        await WriteAuditEventAsync(
            auditStore,
            auditLogger,
            context,
            eventType: "heartbeat.accepted",
            result: session.HasLeaseGap ? "Degraded" : "Accepted",
            access: TryGetAccessContext(context),
            institutionId: institutionId,
            sessionId: session.Id.Value,
            assessmentId: session.AssessmentId,
            metadata: CreateMetadata(
                ("lastKnownEventHash", request.LastKnownEventHash),
                ("leaseExpiresAt", response.LeaseExpiresAt.ToString("o"))),
            cancellationToken: cancellationToken);
        if (session.HasLeaseGap)
        {
            await WriteAuditEventAsync(
                auditStore,
                auditLogger,
                context,
                eventType: "lease.gap.detected",
                result: "Degraded",
                access: TryGetAccessContext(context),
                institutionId: institutionId,
                sessionId: session.Id.Value,
                assessmentId: session.AssessmentId,
                metadata: CreateMetadata(
                    ("maxLeaseGapSeconds", session.MaxLeaseGapSeconds.ToString()),
                    ("firstLeaseGapAt", session.FirstLeaseGapAt?.ToString("o"))),
                cancellationToken: cancellationToken);
        }

        return Results.Ok(response);
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound($"Unknown assessment session: {id}");
    }
});

app.MapPost("/sessions/{id}/checkpoints", async (string id, CheckpointRequest request, HttpRequest httpRequest,
    HttpContext context, IVerifierStorage storage, IVerifierAuditStore auditStore, CancellationToken cancellationToken) =>
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
        var session = await storage.GetSessionAsync(new AssessmentSessionId(request.SessionId), cancellationToken);
        var institutionId = NormalizeInstitutionIdValue(TryGetInstitutionIdFromMetadata(session.MetadataJson));
        await WriteAuditEventAsync(
            auditStore,
            auditLogger,
            context,
            eventType: "checkpoint.accepted",
            result: session.HasLeaseGap ? "Degraded" : "Accepted",
            access: TryGetAccessContext(context),
            institutionId: institutionId,
            sessionId: request.SessionId,
            assessmentId: session.AssessmentId,
            metadata: CreateMetadata(
                ("sequenceNumber", request.SequenceNumber.ToString()),
                ("timelineHeadHash", request.TimelineHeadHash),
                ("receiptHash", receipt.ReceiptHash),
                ("idempotencyKey", idempotencyKey)),
            cancellationToken: cancellationToken);
        if (session.HasLeaseGap)
        {
            await WriteAuditEventAsync(
                auditStore,
                auditLogger,
                context,
                eventType: "lease.gap.detected",
                result: "Degraded",
                access: TryGetAccessContext(context),
                institutionId: institutionId,
                sessionId: request.SessionId,
                assessmentId: session.AssessmentId,
                metadata: CreateMetadata(
                    ("maxLeaseGapSeconds", session.MaxLeaseGapSeconds.ToString()),
                    ("firstLeaseGapAt", session.FirstLeaseGapAt?.ToString("o"))),
                cancellationToken: cancellationToken);
        }
        return Results.Ok(receipt);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(exception.Message);
    }
});

app.MapPost("/packages", async (HttpRequest request, HttpContext context, IPackageSubmissionStore packageStore,
    IVerifierStorage storage, IVerifierAuditStore auditStore, IPackageBlobStore blobStore,
    IPackageVerificationJobStore jobStore, CancellationToken cancellationToken) =>
{
    if (request.HasFormContentType)
    {
        return await HandlePackageUploadAsync(
            request,
            context,
            packageStore,
            storage,
            auditStore,
            blobStore,
            jobStore,
            cancellationToken);
    }

    var idempotencyKey = request.Headers["Idempotency-Key"].FirstOrDefault();
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

    var derivedContext = await ResolvePackageContextFromSessionAsync(
        storage,
        jsonRequest.SessionId,
        jsonRequest.InstitutionId,
        jsonRequest.AssessmentId,
        jsonRequest.StudentUserId,
        cancellationToken);

    jsonRequest = jsonRequest with
    {
        InstitutionId = derivedContext.InstitutionId,
        AssessmentId = derivedContext.AssessmentId,
        StudentUserId = derivedContext.StudentUserId
    };

    var access = TryGetAccessContext(context);
    if (access is not null && !IsOperatorRole(access.Role))
    {
        if (string.IsNullOrWhiteSpace(jsonRequest.InstitutionId) ||
            !string.Equals(jsonRequest.InstitutionId, access.InstitutionId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(
                new { error = $"{access.Role} may only submit packages for their own institution." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (IsStudentClientRole(access.Role) && !string.IsNullOrWhiteSpace(access.StudentUserId))
        {
            if (string.IsNullOrWhiteSpace(jsonRequest.StudentUserId) ||
                !string.Equals(jsonRequest.StudentUserId, access.StudentUserId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    new { error = "StudentClient bound key must match the student user ID of the package." },
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }
    }

    try
    {
        var response = await packageStore.SubmitAsync(jsonRequest, cancellationToken);
        await WriteAuditEventAsync(
            auditStore,
            auditLogger,
            context,
            eventType: "package.submitted",
            result: "Registered",
            access: TryGetAccessContext(context),
            institutionId: response.InstitutionId,
            sessionId: response.SessionId,
            packageId: response.SubmissionId,
            assessmentId: response.AssessmentId,
            metadata: CreateMetadata(
                ("storageProvider", response.ObjectStorageProvider),
                ("objectBucket", response.ObjectBucket),
                ("objectKey", response.ObjectKey),
                ("idempotencyKey", response.IdempotencyKey)),
            cancellationToken: cancellationToken);
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
});

app.MapPost("/packages/upload", async (HttpRequest request, HttpContext context, IPackageSubmissionStore packageStore,
    IVerifierStorage storage, IVerifierAuditStore auditStore, IPackageBlobStore blobStore,
    IPackageVerificationJobStore jobStore, CancellationToken cancellationToken) =>
    await HandlePackageUploadAsync(
        request,
        context,
        packageStore,
        storage,
        auditStore,
        blobStore,
        jobStore,
        cancellationToken));

app.MapPut("/packages/{id}", async (string id, HttpRequest request, HttpContext context,
    IPackageSubmissionStore packageStore, IVerifierAuditStore auditStore, IPackageBlobStore blobStore,
    IPackageVerificationJobStore jobStore, CancellationToken cancellationToken) =>
{
    var submission = await packageStore.GetAsync(id, cancellationToken);
    if (submission is null)
    {
        return Results.NotFound("Unknown package submission.");
    }

    if (!request.HasFormContentType)
    {
        return Results.BadRequest("A file upload is required.");
    }

    var form = await request.ReadFormAsync(cancellationToken);
    if (form.Files.GetFile("file") is not { } file)
    {
        return Results.BadRequest("A file upload is required.");
    }

    var extension = Path.GetExtension(file.FileName);
    if (!string.Equals(extension, ".owspkg", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest("Only .owspkg files are accepted.");
    }

    await WriteAuditEventAsync(
        auditStore,
        auditLogger,
        context,
        eventType: "package.upload.started",
        result: "Started",
        access: TryGetAccessContext(context),
        institutionId: submission.InstitutionId,
        sessionId: submission.SessionId,
        packageId: submission.SubmissionId,
        assessmentId: submission.AssessmentId,
        cancellationToken: cancellationToken);

    try
    {
        await using var source = file.OpenReadStream();
        var savedBlob = await blobStore.SaveAsync(source, cancellationToken);
        if (!string.Equals(savedBlob.PackageSha256, submission.PackageSha256, StringComparison.OrdinalIgnoreCase))
        {
            await WriteAuditEventAsync(
                auditStore,
                auditLogger,
                context,
                eventType: "package.upload.failed",
                result: "HashMismatch",
                access: TryGetAccessContext(context),
                institutionId: submission.InstitutionId,
                sessionId: submission.SessionId,
                packageId: submission.SubmissionId,
                assessmentId: submission.AssessmentId,
                metadata: CreateMetadata(("packageSha256", savedBlob.PackageSha256)),
                cancellationToken: cancellationToken);
            return Results.BadRequest("Uploaded package hash does not match the registered package SHA-256 metadata.");
        }

        var job = await QueuePackageVerificationAsync(
            submission.SubmissionId,
            submission,
            TryGetAccessContext(context),
            packageStore,
            jobStore,
            auditStore,
            context,
            cancellationToken);

        await WriteAuditEventAsync(
            auditStore,
            auditLogger,
            context,
            eventType: "package.upload.completed",
            result: "Stored",
            access: TryGetAccessContext(context),
            institutionId: submission.InstitutionId,
            sessionId: submission.SessionId,
            packageId: submission.SubmissionId,
            assessmentId: submission.AssessmentId,
            metadata: CreateMetadata(("packageSha256", savedBlob.PackageSha256)),
            cancellationToken: cancellationToken);

        return Results.Ok(new
        {
            submissionId = submission.SubmissionId,
            sessionId = submission.SessionId,
            packageSha256 = savedBlob.PackageSha256,
            verificationStatus = "Pending",
            verificationJobId = job.Id
        });
    }
    catch (InvalidOperationException exception)
    {
        await WriteAuditEventAsync(
            auditStore,
            auditLogger,
            context,
            eventType: "package.upload.failed",
            result: "Rejected",
            access: TryGetAccessContext(context),
            institutionId: submission.InstitutionId,
            sessionId: submission.SessionId,
            packageId: submission.SubmissionId,
            assessmentId: submission.AssessmentId,
            metadata: CreateMetadata(("error", exception.Message)),
            cancellationToken: cancellationToken);
        return Results.BadRequest(exception.Message);
    }
});

app.MapPost("/packages/{id}/verify", async (string id, HttpContext context, IPackageSubmissionStore packageStore,
    IVerifierAuditStore auditStore, IPackageBlobStore blobStore, IPackageVerificationJobStore jobStore,
    CancellationToken cancellationToken) =>
{
    var submission = await packageStore.GetAsync(id, cancellationToken);
    if (submission is null)
    {
        return Results.NotFound("Unknown package submission.");
    }

    if (!await blobStore.ExistsAsync(submission.ObjectKey, cancellationToken))
    {
        return Results.BadRequest("Package bytes are not available for verification. Please upload the package file.");
    }

    var job = await QueuePackageVerificationAsync(
        submission.SubmissionId,
        submission,
        TryGetAccessContext(context),
        packageStore,
        jobStore,
        auditStore,
        context,
        cancellationToken);
    return Results.Ok(new
    {
        submissionId = submission.SubmissionId,
        verificationStatus = "Pending",
        verificationJobId = job.Id
    });
});

app.MapGet("/packages/{id}", async (string id, IPackageSubmissionStore packageStore,
    IPackageBlobStore blobStore, CancellationToken cancellationToken) =>
{
    try
    {
        var submission = await packageStore.GetAsync(id, cancellationToken);
        if (submission is null)
        {
            return Results.NotFound("Unknown package submission.");
        }

        return Results.Ok(new
        {
            submission.SubmissionId,
            submission.SessionId,
            submission.InstitutionId,
            submission.AssessmentId,
            submission.StudentUserId,
            submission.IdempotencyKey,
            submission.ObjectStorageProvider,
            submission.ObjectBucket,
            submission.ObjectKey,
            submission.PackageSha256,
            submission.PackageSizeBytes,
            submission.SessionHeadReceiptHash,
            submission.SessionHeadEventHash,
            submission.SessionCheckpointCount,
            submission.VerificationStatus,
            submission.VerificationJobId,
            submission.TrustStatus,
            submission.LastVerificationError,
            submission.CreatedAtUtc,
            blobAvailable = await blobStore.ExistsAsync(submission.ObjectKey, cancellationToken)
        });
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

app.MapGet("/packages/{id}/verification", async (string id, HttpContext context, IPackageSubmissionStore packageStore,
    IVerifierAuditStore auditStore, CancellationToken cancellationToken) =>
{
    var submission = await packageStore.GetAsync(id, cancellationToken);
    if (submission is null)
    {
        return Results.NotFound("Unknown package submission.");
    }

    if (string.IsNullOrWhiteSpace(submission.VerificationResultJson))
    {
        return Results.Json(
            new
            {
                status = submission.VerificationStatus,
                error = submission.LastVerificationError
            },
            statusCode: submission.VerificationStatus == "Failed"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status404NotFound);
    }

    await WriteAuditEventAsync(
        auditStore,
        auditLogger,
        context,
        eventType: "report.read",
        result: "Returned",
        access: TryGetAccessContext(context),
        institutionId: submission.InstitutionId,
        sessionId: submission.SessionId,
        packageId: submission.SubmissionId,
        assessmentId: submission.AssessmentId,
        metadata: CreateMetadata(("contentType", "application/json")),
        cancellationToken: cancellationToken);

    return Results.Content(submission.VerificationResultJson, "application/json");
});

app.MapGet("/packages/{id}/report", async (string id, HttpContext context, IPackageSubmissionStore packageStore,
    IVerifierAuditStore auditStore, IReportGenerator reportGenerator, CancellationToken cancellationToken) =>
{
    var submission = await packageStore.GetAsync(id, cancellationToken);
    if (submission is null)
    {
        return Results.NotFound("Unknown package submission.");
    }

    if (string.IsNullOrWhiteSpace(submission.VerificationResultJson))
    {
        return Results.Json(
            new
            {
                status = submission.VerificationStatus,
                error = submission.LastVerificationError
            },
            statusCode: submission.VerificationStatus == "Failed"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status404NotFound);
    }

    var verificationResult = JsonSerializer.Deserialize<VerificationResult>(
        submission.VerificationResultJson,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (verificationResult is null)
    {
        return Results.Problem("Stored verification result is invalid.");
    }

    var report = await reportGenerator.GenerateAsync(
        new ReportRequest
        {
            VerificationResult = verificationResult,
            Format = ReportFormat.Text
        },
        cancellationToken);

    await WriteAuditEventAsync(
        auditStore,
        auditLogger,
        context,
        eventType: "report.read",
        result: "Returned",
        access: TryGetAccessContext(context),
        institutionId: submission.InstitutionId,
        sessionId: submission.SessionId,
        packageId: submission.SubmissionId,
        assessmentId: submission.AssessmentId,
        metadata: CreateMetadata(("contentType", "text/plain")),
        cancellationToken: cancellationToken);

    return Results.Text(report.Content, "text/plain");
});

app.MapGet("/audit/events", async (HttpContext context, string? institutionId, string? sessionId,
    string? packageId, string? eventType, DateTimeOffset? since, int? limit,
    IVerifierAuditStore auditStore, CancellationToken cancellationToken) =>
{
    var callerAccess = TryGetAccessContext(context);

    // InstitutionAdmin can only read their own institution's audit events.
    // Enforce the scope regardless of what the caller supplied.
    var effectiveInstitutionId = IsInstitutionAdminRole(callerAccess?.Role ?? string.Empty)
        ? callerAccess!.InstitutionId
        : institutionId;

    var query = new VerifierAuditQuery
    {
        InstitutionId = effectiveInstitutionId,
        SessionId = sessionId,
        PackageId = packageId,
        EventType = eventType,
        Since = since,
        Limit = limit ?? 100
    };
    return Results.Ok(await auditStore.QueryAsync(query, cancellationToken));
});

app.MapGet("/diagnostics/summary", async (IVerifierAuditStore auditStore, IVerifierApiKeyStore apiKeyStore,
    IPackageVerificationJobStore jobStore, IPackageBlobStore blobStore, CancellationToken cancellationToken) =>
{
    var summary = await auditStore.GetSummaryAsync(cancellationToken);
    var jobSummary = await jobStore.GetSummaryAsync(cancellationToken);
    var packageStorageReady = await blobStore.CheckHealthAsync(cancellationToken);
    var packageStorageConfigured = !string.IsNullOrWhiteSpace(normalizedStorageOptions.LocalStoragePath);
    var packageStorageProvider = DescribePackageStorageProvider(normalizedStorageOptions);
    var instanceMode = DescribeInstanceMode(normalizedStorageOptions.PackageWorkerEnabled);
    var warnings = BuildDeploymentWarnings(normalizedStorageOptions, packageStorageReady);

    // Count blobs in the package storage directory (cheap: directory listing, no file reads).
    // Returns null when the storage root is not configured or not accessible.
    int? packageBlobCount = null;
    if (packageStorageReady && !string.IsNullOrWhiteSpace(normalizedStorageOptions.LocalStoragePath)
        && Directory.Exists(normalizedStorageOptions.LocalStoragePath))
    {
        try
        {
            packageBlobCount = Directory.GetFiles(normalizedStorageOptions.LocalStoragePath, "*.owspkg").Length;
        }
        catch
        {
            // Non-fatal: leave null if enumeration fails (e.g. permission issue mid-request)
        }
    }

    var signingKeyFingerprint = ReceiptChainVerifier.ComputeFingerprint(normalizedStorageOptions.ReceiptSigningKey);
    var signingKeyFingerprintPresent = !string.IsNullOrWhiteSpace(signingKeyFingerprint);

    return Results.Ok(new
    {
        environment = envMode.ToString(),
        instanceMode,
        workerEnabled = normalizedStorageOptions.PackageWorkerEnabled,
        storageProvider = normalizedStorageOptions.Provider,
        packageStorageProvider,
        packageStorageConfigured,
        packageStorageReady,
        packageBlobCount,
        applyMigrationsOnStartup = normalizedStorageOptions.ApplyMigrationsOnStartup,
        warnings,
        signingKeyFingerprintPresent,
        authMode = await ResolveAuthModeAsync(apiKeyStore, configuredApiKeys.Count > 0, cancellationToken),
        metrics = summary,
        packageVerificationJobs = jobSummary
    });
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
app.MapPost("/education/institutions", async (HttpContext context, Institution institution, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    var callerAccess = TryGetAccessContext(context);
    if (callerAccess is not null && !IsOperatorRole(callerAccess.Role))
    {
        if (!string.Equals(institution.Id.Value, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
    }
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
app.MapPost("/education/courses", async (HttpContext context, Course course, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    var callerAccess = TryGetAccessContext(context);
    if (callerAccess is not null && !IsOperatorRole(callerAccess.Role))
    {
        if (!string.Equals(course.InstitutionId.Value, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
    }
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
app.MapPost("/education/class-groups", async (HttpContext context, ClassGroup classGroup, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    var callerAccess = TryGetAccessContext(context);
    if (callerAccess is not null && !IsOperatorRole(callerAccess.Role))
    {
        if (!string.Equals(classGroup.InstitutionId.Value, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
    }
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
app.MapPost("/education/course-offerings", async (HttpContext context, CourseOffering offering, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    var callerAccess = TryGetAccessContext(context);
    if (callerAccess is not null && !IsOperatorRole(callerAccess.Role))
    {
        if (!string.Equals(offering.InstitutionId.Value, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
    }
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
app.MapPost("/education/enrollments", async (HttpContext context, Enrollment enrollment, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    var callerAccess = TryGetAccessContext(context);
    if (callerAccess is not null && !IsOperatorRole(callerAccess.Role))
    {
        var offering = await educationStore.GetCourseOfferingAsync(enrollment.CourseOfferingId, cancellationToken);
        if (offering is null || !string.Equals(offering.InstitutionId.Value, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
    }
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
app.MapPost("/education/assessments", async (HttpContext context, Assessment assessment, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    var callerAccess = TryGetAccessContext(context);
    if (callerAccess is not null && !IsOperatorRole(callerAccess.Role))
    {
        if (!string.Equals(assessment.InstitutionId.Value, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
    }
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
app.MapPost("/education/users", async (HttpContext context, User user, IEducationStore educationStore,
    CancellationToken cancellationToken) =>
{
    var callerAccess = TryGetAccessContext(context);
    if (callerAccess is not null && !IsOperatorRole(callerAccess.Role))
    {
        if (!string.Equals(user.InstitutionId.Value, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
    }
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

// Handles a multipart .owspkg upload, stores it durably, registers metadata, and queues verification.
static async Task<IResult> HandlePackageUploadAsync(
    HttpRequest request,
    HttpContext context,
    IPackageSubmissionStore packageStore,
    IVerifierStorage storage,
    IVerifierAuditStore auditStore,
    IPackageBlobStore blobStore,
    IPackageVerificationJobStore jobStore,
    CancellationToken cancellationToken)
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest("A file upload is required.");
    }

    var form = await request.ReadFormAsync(cancellationToken);
    if (form.Files.GetFile("file") is not { } file)
    {
        return Results.BadRequest("A file upload is required.");
    }

    var extension = Path.GetExtension(file.FileName);
    if (!string.Equals(extension, ".owspkg", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest("Only .owspkg files are accepted.");
    }

    var access = TryGetAccessContext(context);
    var idempotencyKey = request.Headers["Idempotency-Key"].FirstOrDefault();
    var sessionId = form["sessionId"].FirstOrDefault();
    var institutionId = form["institutionId"].FirstOrDefault();
    var assessmentId = form["assessmentId"].FirstOrDefault();
    var studentUserId = form["studentUserId"].FirstOrDefault();

    await WriteAuditEventAsync(
        auditStore,
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit"),
        context,
        eventType: "package.upload.started",
        result: "Started",
        access: access,
        institutionId: institutionId,
        sessionId: sessionId,
        assessmentId: assessmentId,
        cancellationToken: cancellationToken);

    try
    {
        await using var source = file.OpenReadStream();
        var savedBlob = await blobStore.SaveAsync(source, cancellationToken);
        var derivedContext = await ResolvePackageContextFromSessionAsync(
            storage,
            sessionId,
            institutionId,
            assessmentId,
            studentUserId,
            cancellationToken);

        if (access is not null && !IsOperatorRole(access.Role))
        {
            if (string.IsNullOrWhiteSpace(derivedContext.InstitutionId) ||
                !string.Equals(derivedContext.InstitutionId, access.InstitutionId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    new { error = $"{access.Role} may only upload packages for their own institution." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (IsStudentClientRole(access.Role) && !string.IsNullOrWhiteSpace(access.StudentUserId))
            {
                if (string.IsNullOrWhiteSpace(derivedContext.StudentUserId) ||
                    !string.Equals(derivedContext.StudentUserId, access.StudentUserId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = "StudentClient bound key must match the student user ID of the package." },
                        statusCode: StatusCodes.Status403Forbidden);
                }
            }
        }

        var submissionRequest = new VerifierPackageSubmissionRequest
        {
            SessionId = sessionId,
            InstitutionId = derivedContext.InstitutionId,
            AssessmentId = derivedContext.AssessmentId,
            StudentUserId = derivedContext.StudentUserId,
            IdempotencyKey = idempotencyKey,
            ObjectStorageProvider = "local",
            ObjectBucket = "packages",
            ObjectKey = savedBlob.ObjectKey,
            PackageSha256 = savedBlob.PackageSha256,
            PackageSizeBytes = savedBlob.PackageSizeBytes
        };

        VerifierPackageSubmissionResponse submission;
        try
        {
            submission = await packageStore.SubmitAsync(submissionRequest, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            if (exception.Message.Contains("idempotency key already exists", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("already registered with different metadata", StringComparison.OrdinalIgnoreCase))
            {
                await WriteAuditEventAsync(
                    auditStore,
                    context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit"),
                    context,
                    eventType: "package.upload.failed",
                    result: "Conflict",
                    access: access,
                    institutionId: derivedContext.InstitutionId,
                    sessionId: sessionId,
                    assessmentId: derivedContext.AssessmentId,
                    metadata: CreateMetadata(("error", exception.Message)),
                    cancellationToken: cancellationToken);
                return Results.Conflict(exception.Message);
            }

            throw;
        }

        if (submission.VerificationStatus is "Pending" or "Running" or "Completed" &&
            !string.IsNullOrWhiteSpace(submission.VerificationJobId))
        {
            return Results.Ok(new
            {
                submissionId = submission.SubmissionId,
                sessionId = submission.SessionId,
                packageSha256 = submission.PackageSha256,
                verificationStatus = submission.VerificationStatus,
                verificationJobId = submission.VerificationJobId,
                trustStatus = submission.TrustStatus
            });
        }

        PackageVerificationJobRecord job;
        try
        {
            job = await QueuePackageVerificationAsync(
                submission.SubmissionId,
                submission,
                access,
                packageStore,
                jobStore,
                auditStore,
                context,
                cancellationToken);
        }
        catch (Exception exception)
        {
            await packageStore.UpdateVerificationStateAsync(
                submission.SubmissionId,
                "Failed",
                null,
                null,
                null,
                "Package uploaded but verification job queue failed.",
                cancellationToken);
            await WriteAuditEventAsync(
                auditStore,
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit"),
                context,
                eventType: "package.upload.failed",
                result: "JobQueueFailed",
                access: access,
                institutionId: submission.InstitutionId,
                sessionId: submission.SessionId,
                packageId: submission.SubmissionId,
                assessmentId: submission.AssessmentId,
                metadata: CreateMetadata(("error", exception.Message)),
                cancellationToken: cancellationToken);
            return Results.Problem("Package was uploaded but verification job creation failed.");
        }

        await WriteAuditEventAsync(
            auditStore,
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit"),
            context,
            eventType: "package.upload.completed",
            result: "Stored",
            access: access,
            institutionId: submission.InstitutionId,
            sessionId: submission.SessionId,
            packageId: submission.SubmissionId,
            assessmentId: submission.AssessmentId,
            metadata: CreateMetadata(("packageSha256", submission.PackageSha256)),
            cancellationToken: cancellationToken);
        await WriteAuditEventAsync(
            auditStore,
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit"),
            context,
            eventType: "package.submitted",
            result: "Stored",
            access: access,
            institutionId: submission.InstitutionId,
            sessionId: submission.SessionId,
            packageId: submission.SubmissionId,
            assessmentId: submission.AssessmentId,
            metadata: CreateMetadata(
                ("storageProvider", submission.ObjectStorageProvider),
                ("objectBucket", submission.ObjectBucket),
                ("objectKey", submission.ObjectKey)),
            cancellationToken: cancellationToken);

        return Results.Ok(new
        {
            submissionId = submission.SubmissionId,
            sessionId = submission.SessionId,
            packageSha256 = submission.PackageSha256,
            verificationStatus = "Pending",
            verificationJobId = job.Id
        });
    }
    catch (InvalidOperationException exception)
    {
        await WriteAuditEventAsync(
            auditStore,
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit"),
            context,
            eventType: "package.upload.failed",
            result: "Rejected",
            access: access,
            institutionId: institutionId,
            sessionId: sessionId,
            assessmentId: assessmentId,
            metadata: CreateMetadata(("error", exception.Message)),
            cancellationToken: cancellationToken);
        return Results.BadRequest(exception.Message);
    }
}

// Queues verification and mirrors the current job/status onto the package metadata record.
static async Task<PackageVerificationJobRecord> QueuePackageVerificationAsync(
    string packageId,
    VerifierPackageSubmissionResponse submission,
    VerifierAccessContext? access,
    IPackageSubmissionStore packageStore,
    IPackageVerificationJobStore jobStore,
    IVerifierAuditStore auditStore,
    HttpContext context,
    CancellationToken cancellationToken)
{
    var job = await jobStore.QueueAsync(packageId, access?.KeyId, cancellationToken);
    await packageStore.UpdateVerificationStateAsync(
        packageId,
        job.Status == "Running" ? "Running" : "Pending",
        job.Id,
        null,
        null,
        null,
        cancellationToken);
    await WriteAuditEventAsync(
        auditStore,
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit"),
        context,
        eventType: "package.verification.queued",
        result: job.Status,
        access: access,
        institutionId: submission.InstitutionId,
        sessionId: submission.SessionId,
        packageId: submission.SubmissionId,
        assessmentId: submission.AssessmentId,
        metadata: CreateMetadata(("jobId", job.Id)),
        cancellationToken: cancellationToken);
    return job;
}

// Reuses session metadata as the source of truth when upload callers omit institution or student context.
static async Task<(string? InstitutionId, string? AssessmentId, string? StudentUserId)> ResolvePackageContextFromSessionAsync(
    IVerifierStorage storage,
    string? sessionId,
    string? institutionId,
    string? assessmentId,
    string? studentUserId,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        return (institutionId, assessmentId, studentUserId);
    }

    try
    {
        var session = await storage.GetSessionAsync(new AssessmentSessionId(sessionId), cancellationToken);
        return (
            institutionId ?? TryGetMetadataValue(session.MetadataJson, "institutionId"),
            assessmentId ?? session.AssessmentId ?? TryGetMetadataValue(session.MetadataJson, "assessmentId"),
            studentUserId ?? TryGetMetadataValue(session.MetadataJson, "studentUserId"));
    }
    catch (InvalidOperationException)
    {
        return (institutionId, assessmentId, studentUserId);
    }
}

// Returns the current request id, creating a fallback only if middleware was bypassed.
static string GetRequestId(HttpContext context)
{
    if (context.Items.TryGetValue("RequestId", out var value) && value is string requestId &&
        !string.IsNullOrWhiteSpace(requestId))
    {
        return requestId;
    }

    var generated = Guid.NewGuid().ToString("N");
    context.Items["RequestId"] = generated;
    context.Response.Headers["X-Request-Id"] = generated;
    return generated;
}

// Returns the attached verifier access context when authentication already ran.
static VerifierAccessContext? TryGetAccessContext(HttpContext context) =>
    context.Items.TryGetValue("VerifierAccessContext", out var value) ? value as VerifierAccessContext : null;

// Returns a route value string when present.
static string? TryGetRouteValue(HttpContext context, string key) =>
    context.Request.RouteValues.TryGetValue(key, out var value) ? value?.ToString() : null;

// Returns the package route id only for package endpoints.
static string? TryGetPackageRouteId(HttpContext context)
{
    var path = context.Request.Path.Value ?? string.Empty;
    return path.StartsWith("/packages/", StringComparison.OrdinalIgnoreCase) ? TryGetRouteValue(context, "id") : null;
}

// Builds a compact safe metadata dictionary and drops null/blank values.
static IReadOnlyDictionary<string, string?> CreateMetadata(params (string Key, string? Value)[] pairs)
{
    var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    foreach (var (key, value) in pairs)
    {
        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }

    return metadata;
}

// Appends one safe audit event and emits a matching structured log line without secrets.
static async Task WriteAuditEventAsync(
    IVerifierAuditStore auditStore,
    ILogger logger,
    HttpContext context,
    string eventType,
    string result,
    VerifierAccessContext? access = null,
    string? institutionId = null,
    string? sessionId = null,
    string? packageId = null,
    string? assessmentId = null,
    IReadOnlyDictionary<string, string?>? metadata = null,
    string? actorKeyPrefix = null,
    CancellationToken cancellationToken = default)
{
    var auditEvent = new VerifierAuditEvent
    {
        Id = Guid.NewGuid().ToString("N"),
        CreatedAtUtc = DateTimeOffset.UtcNow,
        EventType = eventType,
        ActorKeyId = access?.KeyId,
        ActorKeyPrefix = actorKeyPrefix ?? access?.KeyPrefix,
        ActorRole = access?.Role,
        InstitutionId = institutionId ?? access?.InstitutionId,
        SessionId = sessionId,
        PackageId = packageId,
        AssessmentId = assessmentId,
        Result = result,
        Metadata = metadata ?? CreateMetadata()
    };

    try
    {
        await auditStore.AppendAsync(auditEvent, cancellationToken);
    }
    catch (Exception exception)
    {
        logger.LogWarning(
            exception,
            "Verifier audit persistence failed for eventType={EventType} requestId={RequestId}.",
            auditEvent.EventType,
            GetRequestId(context));
    }

    logger.LogInformation(
        "Verifier audit {EventType} result={Result} requestId={RequestId} role={Role} institutionId={InstitutionId} sessionId={SessionId} packageId={PackageId} assessmentId={AssessmentId} keyPrefix={KeyPrefix}",
        auditEvent.EventType,
        auditEvent.Result,
        GetRequestId(context),
        auditEvent.ActorRole ?? "Anonymous",
        auditEvent.InstitutionId,
        auditEvent.SessionId,
        auditEvent.PackageId,
        auditEvent.AssessmentId,
        auditEvent.ActorKeyPrefix);
}

// Returns a non-secret display prefix from raw API key material.
static string? CreateSafeKeyPrefix(string? rawApiKey) =>
    string.IsNullOrWhiteSpace(rawApiKey) ? null : rawApiKey[..Math.Min(12, rawApiKey.Length)];

// Resolves the current auth mode without exposing key material.
static async Task<string> ResolveAuthModeAsync(
    IVerifierApiKeyStore apiKeyStore,
    bool hasBootstrapKeys,
    CancellationToken cancellationToken)
{
    var hasPersistedKeys = await apiKeyStore.HasActiveKeysAsync(cancellationToken);
    return (hasBootstrapKeys, hasPersistedKeys) switch
    {
        (true, true) => "bootstrap+persistent",
        (true, false) => "bootstrap",
        (false, true) => "persistent",
        _ => "disabled"
    };
}

// Verifies that the education store can serve a minimal read without exposing details.
static async Task<bool> CheckEducationStoreReadyAsync(IEducationStore educationStore, CancellationToken cancellationToken)
{
    try
    {
        _ = await educationStore.GetInstitutionAsync(new InstitutionId("__ready_probe__"), cancellationToken);
        return true;
    }
    catch
    {
        return false;
    }
}

static bool ResolvePackageWorkerEnabled(IConfiguration configuration, bool legacyDefault)
{
    var configuredValue = configuration["PackageVerificationWorker:Enabled"];
    if (bool.TryParse(configuredValue, out var enabled))
    {
        return enabled;
    }

    var legacyValue = configuration["VerifierStorage:PackageWorkerEnabled"];
    return bool.TryParse(legacyValue, out enabled) ? enabled : legacyDefault;
}

static bool ResolveApplyMigrationsOnStartup(IConfiguration configuration, bool defaultValue)
{
    var configuredValue = configuration["VerifierStorage:ApplyMigrationsOnStartup"];
    return bool.TryParse(configuredValue, out var enabled) ? enabled : defaultValue;
}

static string DescribeInstanceMode(bool workerEnabled) => workerEnabled ? "api+worker" : "api-only";

static string DescribePackageStorageProvider(VerifierStorageOptions options) =>
    string.IsNullOrWhiteSpace(options.LocalStoragePath) ? "unconfigured" : "local-file";

static string[] BuildDeploymentWarnings(VerifierStorageOptions options, bool packageStorageReady)
{
    var warnings = new List<string>();
    if (options.PackageWorkerEnabled && !packageStorageReady)
    {
        warnings.Add("Package verification worker is enabled but package storage is unavailable.");
    }

    if (!options.ApplyMigrationsOnStartup &&
        string.Equals(options.Provider, "postgres", StringComparison.OrdinalIgnoreCase))
    {
        warnings.Add("Automatic PostgreSQL migrations are disabled; run 'migrate' before starting all instances.");
    }

    return warnings.ToArray();
}

// ponytail: config-backed keys are enough for v0.1; add a real identity provider only when external operators need user-level auth.
static List<VerifierAccessContext> BuildConfiguredApiKeys(VerifierSecurityOptions options)
{
    var configuredKeys = new List<VerifierAccessContext>();
    if (!string.IsNullOrWhiteSpace(options.ApiKey))
    {
        configuredKeys.Add(new VerifierAccessContext(
            VerifierRolePolicy.Operator,
            null,
            options.ApiKey,
            null,
            CreateSafeKeyPrefix(options.ApiKey)));
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
            apiKey.Key,
            null,
            CreateSafeKeyPrefix(apiKey.Key)));
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

// RBAC policy for non-Operator roles.
// Operator → always authorized (checked before calling this).
// InstitutionAdmin → institution-scoped writes on education, read on packages/sessions/reports, audit scoped.
// InstructorReviewer → read-only, institution-scoped, no auth/audit/diagnostics.
static async Task<bool> IsAuthorizedAsync(
    HttpContext context,
    VerifierAccessContext access,
    CancellationToken cancellationToken)
{
    if (IsOperatorRole(access.Role))
    {
        return true;
    }

    var isAdmin = IsInstitutionAdminRole(access.Role);
    var isReviewer = IsInstructorReviewerRole(access.Role);
    var isStudent = IsStudentClientRole(access.Role);

    var segments = context.Request.Path.Value?
                       .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   ?? [];
    if (segments.Length == 0)
    {
        return false;
    }

    // Auth management: InstitutionAdmin can POST /auth/api-keys (delegation enforced inside handler).
    // Neither role can list all keys, revoke, or access diagnostics.
    if (segments is ["auth", "api-keys"])
    {
        // GET list: operator only.
        if (HttpMethods.IsGet(context.Request.Method)) return false;
        // POST create: InstitutionAdmin allowed (delegation policy enforced in handler).
        return isAdmin;
    }

    if (segments is ["auth", "api-keys", _, "revoke"])
    {
        return false; // operator only
    }

    // Audit events: InstitutionAdmin can read (institution filter forced in endpoint handler).
    if (segments is ["audit", "events"])
    {
        return isAdmin && HttpMethods.IsGet(context.Request.Method);
    }

    // Diagnostics: operator only.
    if (segments is ["diagnostics", "summary"])
    {
        return false;
    }

    var isGet = HttpMethods.IsGet(context.Request.Method);
    var isWrite = !isGet;

    // POST /sessions: InstitutionAdmin and StudentClient may create sessions.
    if (segments is ["sessions"])
    {
        if (isWrite)
        {
            return isAdmin || isStudent;
        }
        return false;
    }

    // Session resources.
    if (segments is ["sessions", _, "heartbeat"] || segments is ["sessions", _, "checkpoints"])
    {
        var sessionId = segments[1];
        if (isWrite)
        {
            return isStudent && await MatchesStudentSessionScopeAsync(
                sessionId,
                access,
                context.RequestServices.GetRequiredService<IVerifierStorage>(),
                cancellationToken);
        }
        return false;
    }

    if (segments is ["sessions", _, "packages"] || segments is ["sessions", _, "receipts"] || segments is ["sessions", _, "head"])
    {
        var sessionId = segments[1];
        if (isWrite) return false;
        if (isStudent)
        {
            return await MatchesStudentSessionScopeAsync(
                sessionId,
                access,
                context.RequestServices.GetRequiredService<IVerifierStorage>(),
                cancellationToken);
        }
        return (isAdmin || isReviewer) && await MatchesInstitutionScopeAsync(
            null,
            sessionId,
            access,
            context.RequestServices.GetRequiredService<IVerifierStorage>(),
            cancellationToken);
    }

    // Package upload and verify endpoints.
    if (segments is ["packages"] or ["packages", "upload"])
    {
        if (isWrite)
        {
            return isStudent;
        }
        return false;
    }

    if (segments is ["packages", _, "verify"])
    {
        var packageId = segments[1];
        if (isWrite)
        {
            return isStudent && await MatchesStudentPackageScopeAsync(
                packageId,
                access,
                context.RequestServices.GetRequiredService<IPackageSubmissionStore>(),
                cancellationToken);
        }
        return false;
    }

    // Package resources: read/write package itself or its report/verification.
    if (segments is ["packages", _] || segments is ["packages", _, "verification"] || segments is ["packages", _, "report"])
    {
        var packageId = segments[1];
        if (isWrite)
        {
            // PUT /packages/{id}
            if (segments.Length == 2)
            {
                return isStudent && await MatchesStudentPackageScopeAsync(
                    packageId,
                    access,
                    context.RequestServices.GetRequiredService<IPackageSubmissionStore>(),
                    cancellationToken);
            }
            return false;
        }

        // GET requests
        if (isStudent)
        {
            return await MatchesStudentPackageScopeAsync(
                packageId,
                access,
                context.RequestServices.GetRequiredService<IPackageSubmissionStore>(),
                cancellationToken);
        }

        var packageStore = context.RequestServices.GetRequiredService<IPackageSubmissionStore>();
        var submission = await packageStore.GetAsync(packageId, cancellationToken);
        return submission is not null && (isAdmin || isReviewer) && await MatchesInstitutionScopeAsync(
            submission.InstitutionId,
            submission.SessionId,
            access,
            context.RequestServices.GetRequiredService<IVerifierStorage>(),
            cancellationToken);
    }

    // Education endpoints.
    if (segments.Length >= 2 && string.Equals(segments[0], "education", StringComparison.OrdinalIgnoreCase))
    {
        var educationStore = context.RequestServices.GetRequiredService<IEducationStore>();

        if (isWrite)
        {
            // Only InstitutionAdmin can write education data (for their own institution).
            if (!isAdmin) return false;

            // For POST (create) operations on collection endpoints, we enforce institution
            // matching at request body level; trust that the endpoint validates InstitutionId.
            // For reads on specific resources, resolve from the store.
            // Collection-level POSTs (no ID segment): allow; body validation enforces scope.
            if (segments.Length == 2)
            {
                return true; 
            }

            // Write to a specific resource: resolve institution from store.
            var institutionId = await ResolveEducationInstitutionIdAsync(segments, educationStore, cancellationToken);
            return !string.IsNullOrWhiteSpace(institutionId) &&
                   string.Equals(institutionId, access.InstitutionId, StringComparison.OrdinalIgnoreCase);
        }

        // GET: both InstitutionAdmin and InstructorReviewer can read own institution.
        if (!isAdmin && !isReviewer) return false;
        var resolvedInstitutionId = await ResolveEducationInstitutionIdAsync(segments, educationStore, cancellationToken);
        return !string.IsNullOrWhiteSpace(resolvedInstitutionId) &&
               string.Equals(resolvedInstitutionId, access.InstitutionId, StringComparison.OrdinalIgnoreCase);
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

static async Task<bool> MatchesStudentSessionScopeAsync(
    string sessionId,
    VerifierAccessContext access,
    IVerifierStorage storage,
    CancellationToken cancellationToken)
{
    try
    {
        var session = await storage.GetSessionAsync(new AssessmentSessionId(sessionId), cancellationToken);
        if (session is null)
        {
            return false;
        }

        var sessionInstId = NormalizeInstitutionIdValue(TryGetInstitutionIdFromMetadata(session.MetadataJson));
        if (string.IsNullOrWhiteSpace(sessionInstId) ||
            !string.Equals(sessionInstId, access.InstitutionId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(access.StudentUserId))
        {
            var sessionStudentId = TryGetMetadataValue(session.MetadataJson, "studentUserId");
            if (!string.Equals(sessionStudentId, access.StudentUserId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
    catch
    {
        return false;
    }
}

static async Task<bool> MatchesStudentPackageScopeAsync(
    string packageId,
    VerifierAccessContext access,
    IPackageSubmissionStore packageStore,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(access.StudentUserId))
    {
        // Reject reads if the key is not bound to a student
        return false;
    }

    try
    {
        var submission = await packageStore.GetAsync(packageId, cancellationToken);
        if (submission is null)
        {
            return false;
        }

        var packageInstId = NormalizeInstitutionIdValue(submission.InstitutionId);
        if (string.IsNullOrWhiteSpace(packageInstId) ||
            !string.Equals(packageInstId, access.InstitutionId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(submission.StudentUserId, access.StudentUserId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
    catch
    {
        return false;
    }
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

// Pulls one property value out of session metadata without introducing a second metadata model.
static string? TryGetMetadataValue(string? metadataJson, string propertyName)
{
    if (string.IsNullOrWhiteSpace(metadataJson) || string.IsNullOrWhiteSpace(propertyName))
    {
        return null;
    }

    try
    {
        using var document = JsonDocument.Parse(metadataJson);
        return document.RootElement.TryGetProperty(propertyName, out var element) ? element.GetString() : null;
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

static bool IsInstitutionAdminRole(string role) =>
    VerifierRolePolicy.IsInstitutionAdminRole(role);

static bool IsInstructorReviewerRole(string role) =>
    VerifierRolePolicy.IsInstructorReviewerRole(role);

static bool IsStudentClientRole(string role) =>
    VerifierRolePolicy.IsStudentClientRole(role);

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

/// <summary>
/// Distinguishes the verifier's intended deployment posture.
/// </summary>
public enum VerifierEnvironmentMode
{
    /// <summary>Development workstation mode.</summary>
    Development,
    /// <summary>Local pilot mode.</summary>
    Local,
    /// <summary>Production deployment mode.</summary>
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
    public VerifierAccessContext(string role, string? institutionId, string key, string? keyId = null,
        string? keyPrefix = null, string? studentUserId = null)
    {
        Role = string.IsNullOrWhiteSpace(role) ? VerifierRolePolicy.Operator : VerifierRolePolicy.NormalizeRoleName(role);
        InstitutionId = string.IsNullOrWhiteSpace(institutionId) ? null : institutionId.Trim();
        Key = key;
        KeyId = string.IsNullOrWhiteSpace(keyId) ? null : keyId.Trim();
        KeyPrefix = string.IsNullOrWhiteSpace(keyPrefix)
            ? (string.IsNullOrWhiteSpace(key) ? null : key[..Math.Min(12, key.Length)])
            : keyPrefix.Trim();
        StudentUserId = string.IsNullOrWhiteSpace(studentUserId) ? null : studentUserId.Trim();
    }

    public string Role { get; init; }

    public string? InstitutionId { get; init; }

    public string Key { get; init; }

    public string? KeyId { get; init; }

    public string? KeyPrefix { get; init; }

    public string? StudentUserId { get; init; }
}

/// <summary>
/// Exposes the ASP.NET Core entry point for integration testing.
/// </summary>
public partial class Program
{
}
