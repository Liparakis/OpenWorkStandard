using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ows.Core.Education;

namespace Ows.Verifier.Server;

/// <summary>
/// Provides general server utility helper methods used across the OWS Verifier Server.
/// </summary>
internal static class VerifierServerHelpers {
    /// <summary>
    /// Gets the current request ID from the HTTP context, generating a new one if not present.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A string representing the request ID.</returns>
    public static string GetRequestId(HttpContext context) {
        if (context.Items.TryGetValue("RequestId", out var value) && value is string requestId &&
            !string.IsNullOrWhiteSpace(requestId)) {
            return requestId;
        }

        var generated = Guid.NewGuid().ToString("N");
        context.Items["RequestId"] = generated;
        context.Response.Headers["X-Request-Id"] = generated;
        return generated;
    }

    /// <summary>
    /// Tries to retrieve a route value string by its key.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="key">The route key to search for.</param>
    /// <returns>The route value as a string, or null if not found.</returns>
    public static string? TryGetRouteValue(HttpContext context, string key) =>
        context.Request.RouteValues.TryGetValue(key, out var value) ? value?.ToString() : null;

    /// <summary>
    /// Retrieves the package route ID from the request path if it matches package resources.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The package ID string, or null if not matching package paths.</returns>
    public static string? TryGetPackageRouteId(HttpContext context) {
        var path = context.Request.Path.Value ?? string.Empty;
        return path.StartsWith("/packages/", StringComparison.OrdinalIgnoreCase)
            ? TryGetRouteValue(context, "id")
            : null;
    }

    /// <summary>
    /// Truncates the raw API key to generate a non-secret safe prefix for display and auditing.
    /// </summary>
    /// <param name="rawApiKey">The raw API key string.</param>
    /// <returns>A safe prefix string, or null if key is empty.</returns>
    public static string? CreateSafeKeyPrefix(string? rawApiKey) =>
        string.IsNullOrWhiteSpace(rawApiKey) ? null : rawApiKey[..Math.Min(12, rawApiKey.Length)];

    /// <summary>
    /// Resolves the current authentication mode string representing bootstrap/persisted key status.
    /// </summary>
    /// <param name="apiKeyStore">The verifier API key store.</param>
    /// <param name="hasBootstrapKeys">Whether bootstrap API keys are configured.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A string description of the active authentication mode.</returns>
    public static async Task<string> ResolveAuthModeAsync(
        IVerifierApiKeyStore apiKeyStore,
        bool hasBootstrapKeys,
        CancellationToken cancellationToken) {
        var hasPersistedKeys = await apiKeyStore.HasActiveKeysAsync(cancellationToken);
        return (hasBootstrapKeys, hasPersistedKeys) switch {
            (true, true) => "bootstrap+persistent",
            (true, false) => "bootstrap",
            (false, true) => "persistent",
            _ => "disabled"
        };
    }

    /// <summary>
    /// Describes the OIDC configuration status.
    /// </summary>
    /// <param name="options">The configured OIDC options.</param>
    /// <returns>An instance of <see cref="VerifierOidcStatus"/> indicating which options are configured.</returns>
    public static VerifierOidcStatus DescribeOidcStatus(VerifierOidcOptions options) =>
        new() {
            Enabled = options.Enabled,
            AuthorityConfigured = !string.IsNullOrWhiteSpace(options.Authority),
            AudienceConfigured = !string.IsNullOrWhiteSpace(options.Audience),
            RoleClaimConfigured = !string.IsNullOrWhiteSpace(options.RoleClaim)
        };

    /// <summary>
    /// Configures the JWT bearer authentication options using the OIDC options.
    /// </summary>
    /// <param name="options">The JWT bearer options to configure.</param>
    /// <param name="oidcOptions">The source OIDC options.</param>
    public static void ConfigureOidcJwtBearer(JwtBearerOptions options, VerifierOidcOptions oidcOptions) {
        options.RequireHttpsMetadata = oidcOptions.RequireHttpsMetadata;
        options.SaveToken = false;
        options.MapInboundClaims = false;

        if (!oidcOptions.Enabled) {
            options.TokenValidationParameters = new TokenValidationParameters {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false,
                ValidateIssuerSigningKey = false
            };
            return;
        }

        options.Authority = oidcOptions.Authority;
        options.Audience = oidcOptions.Audience;
        options.TokenValidationParameters = new TokenValidationParameters {
            NameClaimType = oidcOptions.DisplayNameClaim,
            RoleClaimType = oidcOptions.RoleClaim,
            ValidateIssuer = true,
            ValidAudience = oidcOptions.Audience,
            ValidateAudience = !string.IsNullOrWhiteSpace(oidcOptions.Audience)
        };
    }

    /// <summary>
    /// Writes a structured authentication error response as JSON.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="statusCode">The HTTP status code to return.</param>
    /// <param name="error">The short error code string.</param>
    /// <param name="message">The detailed error message.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
    public static async Task WriteAuthErrorAsync(HttpContext context, int statusCode, string error, string message) {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new {
            error,
            message,
            requestId = GetRequestId(context)
        });
    }

    /// <summary>
    /// Performs a simple read probe against the education store to verify readiness.
    /// </summary>
    /// <param name="educationStore">The education store.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the store is reachable and ready; otherwise false.</returns>
    public static async Task<bool> CheckEducationStoreReadyAsync(IEducationStore educationStore,
        CancellationToken cancellationToken) {
        try {
            _ = await educationStore.GetInstitutionAsync(new InstitutionId("__ready_probe__"), cancellationToken);
            return true;
        } catch {
            return false;
        }
    }

    /// <summary>
    /// Resolves whether the package verification background worker is enabled.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="legacyDefault">The legacy fallback default value.</param>
    /// <returns>True if the worker is enabled; otherwise false.</returns>
    public static bool ResolvePackageWorkerEnabled(IConfiguration configuration, bool legacyDefault) {
        var configuredValue = configuration["PackageVerificationWorker:Enabled"];
        if (bool.TryParse(configuredValue, out var enabled)) {
            return enabled;
        }

        var legacyValue = configuration["VerifierStorage:PackageWorkerEnabled"];
        return bool.TryParse(legacyValue, out enabled) ? enabled : legacyDefault;
    }

    /// <summary>
    /// Resolves whether PostgreSQL database migrations should run automatically on startup.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="defaultValue">The default value to use if not configured.</param>
    /// <returns>True if migrations should run; otherwise false.</returns>
    public static bool ResolveApplyMigrationsOnStartup(IConfiguration configuration, bool defaultValue) {
        var configuredValue = configuration["VerifierStorage:ApplyMigrationsOnStartup"];
        return bool.TryParse(configuredValue, out var enabled) ? enabled : defaultValue;
    }

    /// <summary>
    /// Returns a string description of the active server instance mode.
    /// </summary>
    /// <param name="workerEnabled">Whether the package verification worker is enabled.</param>
    /// <returns>A string representation of the instance mode.</returns>
    public static string DescribeInstanceMode(bool workerEnabled) => workerEnabled ? "api+worker" : "api-only";

    /// <summary>
    /// Describes the configured package storage provider.
    /// </summary>
    /// <param name="options">The storage options.</param>
    /// <returns>A string description of the storage provider.</returns>
    public static string DescribePackageStorageProvider(VerifierStorageOptions options) =>
        string.IsNullOrWhiteSpace(options.LocalStoragePath) ? "unconfigured" : "local-file";

    /// <summary>
    /// Inspects options and checks dependency status to compile any startup deployment warnings.
    /// </summary>
    /// <param name="options">The verifier storage options.</param>
    /// <param name="packageStorageReady">Whether the package storage is healthy/reachable.</param>
    /// <returns>An array of deployment warning messages.</returns>
    public static string[] BuildDeploymentWarnings(VerifierStorageOptions options, bool packageStorageReady) {
        var warnings = new List<string>();
        if (options.PackageWorkerEnabled && !packageStorageReady) {
            warnings.Add("Package verification worker is enabled but package storage is unavailable.");
        }

        if (!options.ApplyMigrationsOnStartup &&
            string.Equals(options.Provider, "postgres", StringComparison.OrdinalIgnoreCase)) {
            warnings.Add("Automatic PostgreSQL migrations are disabled; run 'migrate' before starting all instances.");
        }

        return warnings.ToArray();
    }

    /// <summary>
    /// Identifies weak, default, or placeholder secret values that are unsafe for production.
    /// </summary>
    /// <param name="secret">The secret key material to check.</param>
    /// <returns>True if the secret is considered weak; otherwise false.</returns>
    public static bool IsWeakSecret(string secret) {
        if (string.IsNullOrWhiteSpace(secret)) return true;
        var normalized = secret.Trim().ToLowerInvariant();
        if (normalized.Length < 16) return true;

        string[] unsafeDefaults =
            ["dev-key", "change-me", "change_me", "default", "placeholder", "development", "ows-dev", "ows_dev"];
        return unsafeDefaults.Contains(normalized);
    }
}
