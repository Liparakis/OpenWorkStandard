using System.Diagnostics;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Ows.Core.Education;
using Ows.Core.Notarization;
using Ows.Verifier.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();

#region Configuration Loading

var storageOptions = builder.Configuration.GetSection("VerifierStorage").Get<VerifierStorageOptions>()
                     ?? new VerifierStorageOptions();
var packageWorkerEnabled =
    VerifierServerHelpers.ResolvePackageWorkerEnabled(builder.Configuration, storageOptions.PackageWorkerEnabled);
var applyMigrationsOnStartup =
    VerifierServerHelpers.ResolveApplyMigrationsOnStartup(builder.Configuration,
        storageOptions.ApplyMigrationsOnStartup);
var securityOptions = builder.Configuration.GetSection("VerifierSecurity").Get<VerifierSecurityOptions>()
                      ?? new VerifierSecurityOptions();
var authOptions = builder.Configuration.GetSection("VerifierAuth").Get<VerifierAuthOptions>()
                  ?? new VerifierAuthOptions();
var rateLimitingOptions =
    builder.Configuration.GetSection("VerifierRateLimiting").Get<VerifierRateLimitingOptions>()
    ?? new VerifierRateLimitingOptions();
var normalizedStorageOptions = storageOptions with {
    PackageWorkerEnabled = packageWorkerEnabled,
    ApplyMigrationsOnStartup = applyMigrationsOnStartup,
    JsonStorePath = string.IsNullOrWhiteSpace(storageOptions.JsonStorePath)
        ? Path.Combine(builder.Environment.ContentRootPath, ".ows-verifier", "receipts.json")
        : storageOptions.JsonStorePath,
    LocalStoragePath = string.IsNullOrWhiteSpace(storageOptions.LocalStoragePath)
        ? Path.Combine(builder.Environment.ContentRootPath, ".ows-verifier", "packages")
        : storageOptions.LocalStoragePath
};

var envString = builder.Configuration["VerifierEnvironment"] ?? builder.Environment.EnvironmentName;
if (!Enum.TryParse<VerifierEnvironmentMode>(envString, true, out var envMode)) {
    envMode = VerifierEnvironmentMode.Development;
}

var isProduction = envMode == VerifierEnvironmentMode.Production;
var startupWarnings = new List<string>();
var startupErrors = new List<string>();
var configuredApiKeys = VerifierAuthorizationHelpers.BuildConfiguredApiKeys(securityOptions);
var oidcStatus = VerifierServerHelpers.DescribeOidcStatus(authOptions.Oidc);

#endregion

#region Startup Validation

ValidateApiKeys();
ValidateSigningKey();
ValidateOidcConfiguration();
ValidateStorageProvider();

if (startupErrors.Count > 0) {
    foreach (var error in startupErrors) {
        Console.Error.WriteLine($"FATAL CONFIGURATION ERROR: {error}");
    }

    throw new InvalidOperationException("Fatal configuration errors detected in Production mode. See console output.");
}

#endregion

#region Migrate CLI Invocation

if (await RunMigrationIfRequestedAsync()) {
    return;
}

#endregion

#region Host Build and Service Registration

builder.Services.AddVerifierServices(normalizedStorageOptions, securityOptions, authOptions, rateLimitingOptions,
    builder.Environment);

var app = builder.Build();
var requestLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Requests");
var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
var auditLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit");

LogStartupSummary();

#endregion

#region Eager Store Initialization

await InitializeOptionalStorageAsync();
await InitializeOptionalEducationStoreAsync();

var apiKeyStore = app.Services.GetRequiredService<IVerifierApiKeyStore>();
await InitializeApiKeyStoreAsync(apiKeyStore);

var auditStore = app.Services.GetRequiredService<IVerifierAuditStore>();
await InitializeAuditStoreAsync(auditStore);

var packageJobStore = app.Services.GetRequiredService<IPackageVerificationJobStore>();
await InitializePackageJobStoreAsync(packageJobStore);

#endregion

#region Middleware Pipeline

app.Use(RequestIdMiddleware);
app.Use(RequestLoggingMiddleware);
app.UseAuthentication();
app.Use(VerifierAuthenticationMiddleware);
if (rateLimitingOptions.Enabled) {
    app.UseRateLimiter();
}

app.MapVerifierEndpoints();

app.Run();

#endregion

#region Local Functions

void ValidateApiKeys() {
    if (configuredApiKeys.Count == 0) {
        startupWarnings.Add(
            "VerifierSecurity bootstrap API keys are not configured. Persisted API keys or unguarded local bootstrap mode will be used.");
        return;
    }

    var duplicateKeys = configuredApiKeys
        .GroupBy(static key => key.Key, StringComparer.Ordinal)
        .Where(static group => group.Count() > 1)
        .Select(static group => group.Key)
        .ToArray();
    foreach (var duplicateKey in duplicateKeys) {
        startupErrors.Add(
            $"VerifierSecurity contains duplicate API key material for fingerprint {ReceiptChainVerifier.ComputeFingerprint(duplicateKey)}.");
    }

    foreach (var apiKey in configuredApiKeys) {
        if (VerifierServerHelpers.IsWeakSecret(apiKey.Key)) {
            if (isProduction) {
                startupErrors.Add(
                    $"VerifierSecurity key for role '{apiKey.Role}' is too weak/short for Production mode. It must be at least 16 characters long and not a known default.");
            } else {
                startupWarnings.Add($"VerifierSecurity key for role '{apiKey.Role}' is weak or using a dev default.");
            }
        }

        if (!VerifierRolePolicy.IsSupportedRole(apiKey.Role)) {
            startupErrors.Add(
                $"VerifierSecurity role '{apiKey.Role}' is not supported. Use 'Operator' or 'InstructorReviewer'.");
        }

        if (VerifierRolePolicy.IsInstructorReviewerRole(apiKey.Role) && string.IsNullOrWhiteSpace(apiKey.InstitutionId)) {
            startupErrors.Add("VerifierSecurity InstructorReviewer keys must set InstitutionId.");
        }
    }
}

void ValidateSigningKey() {
    if (string.IsNullOrWhiteSpace(normalizedStorageOptions.ReceiptSigningKey)) {
        if (isProduction) {
            startupErrors.Add("VerifierStorage:ReceiptSigningKey must be configured in Production mode.");
        } else {
            startupWarnings.Add("VerifierStorage:ReceiptSigningKey is not configured. Receipts will not be signed.");
        }

        return;
    }

    if (VerifierServerHelpers.IsWeakSecret(normalizedStorageOptions.ReceiptSigningKey)) {
        if (isProduction) {
            startupErrors.Add(
                "VerifierStorage:ReceiptSigningKey is too weak/short for Production mode. It must be at least 16 characters long and not a known default.");
        } else {
            startupWarnings.Add("VerifierStorage:ReceiptSigningKey is weak or using a dev default.");
        }
    }
}

void ValidateOidcConfiguration() {
    if (!authOptions.Oidc.Enabled) {
        return;
    }

    if (!oidcStatus.AuthorityConfigured) {
        startupErrors.Add("VerifierAuth:Oidc:Authority must be configured when OIDC/JWT bearer auth is enabled.");
    }

    if (!oidcStatus.AudienceConfigured) {
        startupErrors.Add("VerifierAuth:Oidc:Audience must be configured when OIDC/JWT bearer auth is enabled.");
    }

    if (!oidcStatus.RoleClaimConfigured) {
        startupErrors.Add("VerifierAuth:Oidc:RoleClaim must be configured when OIDC/JWT bearer auth is enabled.");
    }
}

void ValidateStorageProvider() {
    if (string.Equals(normalizedStorageOptions.Provider, "json", StringComparison.OrdinalIgnoreCase)) {
        if (isProduction) {
            startupErrors.Add("JSON storage provider is not allowed in Production mode. Use 'postgres' provider.");
        } else {
            startupWarnings.Add("Using JSON file storage provider. This is only suitable for development/local use.");
        }
    }

    if (string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase) &&
        normalizedStorageOptions.ApplyMigrationsOnStartup) {
        startupWarnings.Add(
            "VerifierStorage:ApplyMigrationsOnStartup is enabled. Multi-instance deployments should run 'migrate' once, then set it to false on steady-state instances.");
    }
}

async Task<bool> RunMigrationIfRequestedAsync() {
    if (!args.Any(static arg => string.Equals(arg, "migrate", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(arg, "--migrate", StringComparison.OrdinalIgnoreCase))) {
        return false;
    }

    if (!string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)) {
        Console.WriteLine("Verifier migration is only supported when VerifierStorage:Provider=postgres.");
        return true;
    }

    if (string.IsNullOrWhiteSpace(normalizedStorageOptions.PostgresConnectionString)) {
        throw new InvalidOperationException(
            "VerifierStorage:PostgresConnectionString must be configured when VerifierStorage:Provider=postgres.");
    }

    await PostgresVerifierMigrator.MigrateAsync(normalizedStorageOptions.PostgresConnectionString);
    Console.WriteLine("Verifier schema migration complete.");
    return true;
}

void LogStartupSummary() {
    startupLogger.LogInformation("OWS Verifier starting up...");
    startupLogger.LogInformation("Environment Mode: {EnvironmentMode}", envMode);
    startupLogger.LogInformation("Storage Provider: {Provider}", normalizedStorageOptions.Provider);
    startupLogger.LogInformation("Instance Mode: {InstanceMode}",
        VerifierServerHelpers.DescribeInstanceMode(normalizedStorageOptions.PackageWorkerEnabled));
    startupLogger.LogInformation("Package Verification Worker Enabled: {WorkerEnabled}",
        normalizedStorageOptions.PackageWorkerEnabled);
    startupLogger.LogInformation("Apply Migrations On Startup: {ApplyMigrationsOnStartup}",
        normalizedStorageOptions.ApplyMigrationsOnStartup);
    startupLogger.LogInformation("API Guard: {ApiGuardStatus}", configuredApiKeys.Count == 0 ? "Disabled" : "Enabled");
    startupLogger.LogInformation(
        "OIDC/JWT Bearer Foundation: enabled={Enabled} authorityConfigured={AuthorityConfigured} audienceConfigured={AudienceConfigured} roleClaimConfigured={RoleClaimConfigured}",
        oidcStatus.Enabled,
        oidcStatus.AuthorityConfigured,
        oidcStatus.AudienceConfigured,
        oidcStatus.RoleClaimConfigured);
    if (configuredApiKeys.Count > 0) {
        startupLogger.LogInformation("API Header Name: {HeaderName}", securityOptions.HeaderName);
        startupLogger.LogInformation("Configured API Keys: {ApiKeyCount}", configuredApiKeys.Count);
    }

    var keyFingerprint = ReceiptChainVerifier.ComputeFingerprint(normalizedStorageOptions.ReceiptSigningKey);
    startupLogger.LogInformation("Signing Key Fingerprint: {Fingerprint}",
        string.IsNullOrWhiteSpace(keyFingerprint) ? "None (Unsigned)" : keyFingerprint);

    foreach (var warning in startupWarnings) {
        startupLogger.LogWarning("CONFIGURATION WARNING: {Warning}", warning);
    }
}

async Task InitializeOptionalStorageAsync() {
    if (app.Services.GetService<IVerifierStorage>() is not { } storage) {
        return;
    }

    try {
        startupLogger.LogInformation("Initializing verifier storage...");
        await storage.InitializeAsync(CancellationToken.None);
        startupLogger.LogInformation(
            "Verifier storage initialized successfully ({InitializationMode}).",
            normalizedStorageOptions.ApplyMigrationsOnStartup ? "database/migrations ready" : "database access ready");
    } catch (Exception ex) {
        startupLogger.LogError(ex, "Failed to initialize verifier storage.");
        if (isProduction) {
            throw;
        }
    }
}

async Task InitializeOptionalEducationStoreAsync() {
    if (app.Services.GetService<IEducationStore>() is not { } educationStore) {
        return;
    }

    try {
        startupLogger.LogInformation("Initializing education store...");
        await educationStore.InitializeAsync(CancellationToken.None);
        startupLogger.LogInformation("Education store initialized successfully.");
    } catch (Exception ex) {
        startupLogger.LogError(ex, "Failed to initialize education store.");
        if (isProduction) {
            throw;
        }
    }
}

async Task InitializeApiKeyStoreAsync(IVerifierApiKeyStore store) {
    try {
        startupLogger.LogInformation("Initializing verifier API key store...");
        await store.InitializeAsync(CancellationToken.None);
        var hasPersistedKeys = await store.HasActiveKeysAsync(CancellationToken.None);
        startupLogger.LogInformation(
            "Verifier API key store initialized successfully. Persisted Keys Present: {HasPersistedKeys}",
            hasPersistedKeys);
        if (isProduction && configuredApiKeys.Count == 0 && !hasPersistedKeys) {
            throw new InvalidOperationException(
                "Production mode requires either bootstrap API keys or persisted verifier API keys.");
        }
    } catch (Exception ex) {
        startupLogger.LogError(ex, "Failed to initialize verifier API key store.");
        if (isProduction) {
            throw;
        }
    }
}

async Task InitializeAuditStoreAsync(IVerifierAuditStore store) {
    try {
        startupLogger.LogInformation("Initializing verifier audit store...");
        await store.InitializeAsync(CancellationToken.None);
        startupLogger.LogInformation("Verifier audit store initialized successfully.");
    } catch (Exception ex) {
        startupLogger.LogError(ex, "Failed to initialize verifier audit store.");
        if (isProduction) {
            throw;
        }
    }
}

async Task InitializePackageJobStoreAsync(IPackageVerificationJobStore store) {
    try {
        startupLogger.LogInformation("Initializing package verification job store...");
        await store.InitializeAsync(CancellationToken.None);
        startupLogger.LogInformation("Package verification job store initialized successfully.");
    } catch (Exception ex) {
        startupLogger.LogError(ex, "Failed to initialize package verification job store.");
        if (isProduction) {
            throw;
        }
    }
}

async Task RequestIdMiddleware(HttpContext context, RequestDelegate next) {
    var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(requestId)) {
        requestId = Guid.NewGuid().ToString("N");
    }

    context.Items["RequestId"] = requestId;
    context.Response.Headers["X-Request-Id"] = requestId;
    await next(context);
}

async Task RequestLoggingMiddleware(HttpContext context, RequestDelegate next) {
    var stopwatch = Stopwatch.StartNew();
    try {
        await next(context);
    } finally {
        stopwatch.Stop();

        var accessContext = VerifierAuthorizationHelpers.TryGetAccessContext(context);
        requestLogger.LogInformation(
            "Verifier request {RequestId} {Method} {Path} returned {StatusCode} in {ElapsedMilliseconds} ms. authType={AuthType} role={Role} institutionId={InstitutionId} keyPrefix={KeyPrefix}",
            VerifierServerHelpers.GetRequestId(context),
            context.Request.Method,
            context.Request.Path.Value,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            accessContext?.AuthenticationType ?? "Anonymous",
            accessContext?.Role ?? "Anonymous",
            accessContext?.InstitutionId,
            accessContext?.KeyPrefix);
    }
}

async Task VerifierAuthenticationMiddleware(HttpContext context, RequestDelegate next) {
    var suppliedKey = VerifierAuthorizationHelpers.TryGetSuppliedApiKey(context.Request, securityOptions.HeaderName);
    var suppliedBearerToken = VerifierAuthorizationHelpers.TryGetSuppliedBearerToken(context.Request);
    if (suppliedKey is not null && suppliedBearerToken is not null) {
        await VerifierAuditHelpers.WriteAuditEventAsync(
            context.RequestServices.GetRequiredService<IVerifierAuditStore>(),
            auditLogger,
            context,
            eventType: "auth.ambiguous",
            result: "DualCredentialsRejected",
            metadata: VerifierAuditHelpers.CreateMetadata(
                ("endpoint", context.Request.Path.Value),
                ("method", context.Request.Method),
                ("authenticationType", "Dual")),
            actorKeyPrefix: VerifierServerHelpers.CreateSafeKeyPrefix(suppliedKey),
            cancellationToken: context.RequestAborted);
        await VerifierServerHelpers.WriteAuthErrorAsync(
            context,
            StatusCodes.Status400BadRequest,
            "ambiguous_authentication",
            "Send either X-OWS-Verifier-Key or Authorization: Bearer, not both.");
        return;
    }

    var path = context.Request.Path.Value ?? string.Empty;
    var isBypassPath = HttpMethods.IsGet(context.Request.Method) &&
                       (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(path, "/ready", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(path, "/metrics", StringComparison.OrdinalIgnoreCase));
    if (isBypassPath) {
        await next(context);
        return;
    }

    if (suppliedBearerToken is not null && !authOptions.Oidc.Enabled) {
        await VerifierAuditHelpers.WriteAuditEventAsync(
            context.RequestServices.GetRequiredService<IVerifierAuditStore>(),
            auditLogger,
            context,
            eventType: "auth.failed",
            result: "OidcDisabled",
            metadata: VerifierAuditHelpers.CreateMetadata(
                ("endpoint", context.Request.Path.Value),
                ("method", context.Request.Method),
                ("authenticationType", "Bearer")),
            cancellationToken: context.RequestAborted);
        await VerifierServerHelpers.WriteAuthErrorAsync(
            context,
            StatusCodes.Status401Unauthorized,
            "oidc_disabled",
            "OIDC/JWT bearer auth is not enabled on this verifier.");
        return;
    }

    var persistentApiKeyStore = context.RequestServices.GetRequiredService<IVerifierApiKeyStore>();
    var hasBootstrapKeys = configuredApiKeys.Count > 0;
    var hasPersistedKeys = false;
    try {
        hasPersistedKeys = await persistentApiKeyStore.HasActiveKeysAsync(context.RequestAborted);
    } catch when (!isProduction) {
        if (!hasBootstrapKeys && !authOptions.Oidc.Enabled) {
            await next(context);
            return;
        }
    }

    if (!hasBootstrapKeys && !hasPersistedKeys && !authOptions.Oidc.Enabled) {
        await next(context);
        return;
    }

    VerifierAccessContext? access = null;
    var authFailureResult = authOptions.Oidc.Enabled ? "MissingCredentials" : "MissingKey";
    var authFailureMessage = authOptions.Oidc.Enabled
        ? "Send either X-OWS-Verifier-Key or Authorization: Bearer."
        : "Verifier API key is required.";
    var authFailureStatusCode = StatusCodes.Status401Unauthorized;
    var authFailureCode = authOptions.Oidc.Enabled ? "authentication_required" : null;
    string? authFailureActorKeyPrefix = null;
    var authFailureMetadata = VerifierAuditHelpers.CreateMetadata(
        ("endpoint", context.Request.Path.Value),
        ("method", context.Request.Method));

    if (suppliedKey is not null) {
        access = VerifierAuthorizationHelpers.TryAuthenticateConfiguredApiKey(suppliedKey, configuredApiKeys)
                 ?? await VerifierAuthorizationHelpers.TryAuthenticatePersistedApiKeyAsync(persistentApiKeyStore,
                     suppliedKey,
                     context.RequestAborted);
        authFailureResult = "InvalidKey";
        authFailureMessage = "Invalid verifier API key.";
        authFailureActorKeyPrefix = VerifierServerHelpers.CreateSafeKeyPrefix(suppliedKey);
        authFailureMetadata = VerifierAuditHelpers.CreateMetadata(
            ("endpoint", context.Request.Path.Value),
            ("method", context.Request.Method),
            ("authenticationType", "ApiKey"));
    } else if (suppliedBearerToken is not null) {
        authFailureMetadata = VerifierAuditHelpers.CreateMetadata(
            ("endpoint", context.Request.Path.Value),
            ("method", context.Request.Method),
            ("authenticationType", "Bearer"));

        if (!authOptions.Oidc.Enabled) {
            authFailureResult = "OidcDisabled";
            authFailureMessage = "OIDC/JWT bearer auth is not enabled on this verifier.";
            authFailureCode = "oidc_disabled";
        } else {
            var authenticateResult = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            if (authenticateResult is { Succeeded: true, Principal: not null }) {
                var mappingResult = OidcPrincipalMapper.TryMap(authenticateResult.Principal, authOptions.Oidc);
                if (mappingResult.Succeeded) {
                    access = mappingResult.AccessContext;
                } else {
                    authFailureResult = mappingResult.FailureResult ?? "InvalidBearerClaims";
                    authFailureMessage = mappingResult.FailureMessage ?? "Bearer token claims were not accepted.";
                    authFailureStatusCode = mappingResult.StatusCode;
                    authFailureCode = "invalid_oidc_claims";
                }
            } else {
                authFailureResult = "InvalidBearerToken";
                authFailureMessage = "Bearer token validation failed.";
                authFailureCode = "invalid_bearer_token";
            }
        }
    }

    if (access is null) {
        await VerifierAuditHelpers.WriteAuditEventAsync(
            auditStore,
            auditLogger,
            context,
            eventType: "auth.failed",
            result: authFailureResult,
            metadata: authFailureMetadata,
            actorKeyPrefix: authFailureActorKeyPrefix,
            cancellationToken: context.RequestAborted);

        if (authFailureCode is null) {
            context.Response.StatusCode = authFailureStatusCode;
            await context.Response.WriteAsync(authFailureMessage);
        } else {
            await VerifierServerHelpers.WriteAuthErrorAsync(context, authFailureStatusCode, authFailureCode,
                authFailureMessage);
        }

        return;
    }

    if (!await VerifierAuthorizationHelpers.IsAuthorizedAsync(context, access, context.RequestAborted)) {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await VerifierAuditHelpers.WriteAuditEventAsync(
            auditStore,
            auditLogger,
            context,
            eventType: "access.denied",
            result: "Forbidden",
            access: access,
            institutionId: access.InstitutionId,
            sessionId: VerifierServerHelpers.TryGetRouteValue(context, "id"),
            packageId: VerifierServerHelpers.TryGetPackageRouteId(context),
            metadata: VerifierAuditHelpers.CreateMetadata(
                ("endpoint", context.Request.Path.Value),
                ("method", context.Request.Method)),
            cancellationToken: context.RequestAborted);
        await context.Response.WriteAsync("Verifier API key is not authorized for this resource.");
        return;
    }

    context.Items["VerifierAccessContext"] = access;
    await next(context);
}

#endregion

#region Types and DTOs

/// <summary>
/// Distinguishes the verifier's intended deployment posture.
/// </summary>
public enum VerifierEnvironmentMode {
    /// <summary>Development workstation mode.</summary>
    Development,

    /// <summary>Local pilot mode.</summary>
    Local,

    /// <summary>Production deployment mode.</summary>
    Production
}

// ReSharper disable UnusedAutoPropertyAccessor.Global
/// <summary>
/// Represents the optional education context that may be supplied when starting a new verifier session.
/// </summary>
public sealed record StartSessionRequest {
    /// <summary>Gets the optional institution identifier.</summary>
    public string? InstitutionId { get; init; }

    /// <summary>Gets the optional course offering identifier.</summary>
    public string? CourseOfferingId { get; init; }

    /// <summary>Gets the optional assessment identifier.</summary>
    public string? AssessmentId { get; init; }

    /// <summary>Gets the optional student user identifier.</summary>
    public string? StudentUserId { get; init; }
}

/// <summary>
/// Exposes the ASP.NET Core entry point for integration testing.
/// </summary>
public partial class Program {
}

#endregion
