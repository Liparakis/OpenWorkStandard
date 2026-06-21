namespace Ows.Verifier.Server;

/// <summary>
/// Provides extension methods for registering all OWS Verifier Server services.
/// </summary>
public static class VerifierServiceCollectionExtensions {
    /// <summary>
    /// Registers all storage, security/auth, observability, and background worker dependencies in a single consolidated call.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="storageOptions">The configured storage options.</param>
    /// <param name="securityOptions">The configured security options.</param>
    /// <param name="authOptions">The configured OIDC/JWT auth options.</param>
    /// <param name="rateLimitingOptions">The configured HTTP rate limiting options.</param>
    /// <param name="environment">The hosting environment.</param>
    /// <returns>The modified service collection.</returns>
    public static void AddVerifierServices(this IServiceCollection services,
        VerifierStorageOptions storageOptions,
        VerifierSecurityOptions securityOptions,
        VerifierAuthOptions authOptions,
        VerifierRateLimitingOptions rateLimitingOptions,
        IWebHostEnvironment environment) {
        services.AddVerifierStorage(storageOptions, environment);
        services.AddVerifierAuth(securityOptions, authOptions);
        services.AddVerifierRateLimiting(rateLimitingOptions, storageOptions);
        services.AddVerifierObservability();
        services.AddVerifierBackgroundWorker(storageOptions);
    }
}
