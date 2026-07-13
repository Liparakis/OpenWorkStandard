using Ows.Core.Notarization;
using Ows.Verifier.Server.Helpers;

namespace Ows.Verifier.Server;

/// <summary>
/// Provides route endpoint mapping extension methods for system diagnostics and readiness probes.
/// </summary>
internal static class VerifierDiagnosticsEndpoints {
    /// <summary>
    /// Maps the readiness (`/ready`) and diagnostics metadata summary (`/diagnostics/summary`) endpoints.
    /// </summary>
    /// <param name="app">The route builder application instance.</param>
    /// <returns>The route builder with endpoints mapped.</returns>
    public static void MapVerifierDiagnosticsEndpoints(this IEndpointRouteBuilder app) {
        var auditLogger = app.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit");

        app.MapGet("/ready",
            async (HttpContext context, IVerifierStorage storage,
                IVerifierApiKeyStore apiKeyStore, IPackageBlobStore blobStore, VerifierStorageOptions options,
                VerifierSecurityOptions securityOptions, VerifierAuthOptions authOptions,
                IConfiguration configuration, IWebHostEnvironment environment, CancellationToken cancellationToken) => {
                    var configuredApiKeys = VerifierAuthorizationHelpers.BuildConfiguredApiKeys(securityOptions);
                    var signingConfigured = !string.IsNullOrWhiteSpace(options.ReceiptSigningKey);
                    var packageStorageConfigured = !string.IsNullOrWhiteSpace(options.LocalStoragePath);
                    var packageStorageProvider = VerifierServerHelpers.DescribePackageStorageProvider(options);
                    var instanceMode = VerifierServerHelpers.DescribeInstanceMode(options.PackageWorkerEnabled);
                    try {
                        var authMode = await VerifierServerHelpers.ResolveAuthModeAsync(apiKeyStore,
                            configuredApiKeys.Count > 0, cancellationToken);
                        var oidc = VerifierServerHelpers.DescribeOidcStatus(authOptions.Oidc);
                        var healthy = await storage.CheckHealthAsync(cancellationToken);
                        var packageStorageReady = await blobStore.CheckHealthAsync(cancellationToken);
                        var warnings = VerifierServerHelpers.BuildDeploymentWarnings(options, packageStorageReady);
                        if (!healthy || !packageStorageReady) {
                            await VerifierAuditHelpers.WriteAuditEventAsync(
                                context.RequestServices.GetRequiredService<IVerifierAuditStore>(),
                                auditLogger,
                                context,
                                eventType: "readiness.failed",
                                result: "Unhealthy",
                                metadata: VerifierAuditHelpers.CreateMetadata(
                                    ("storageProvider", options.Provider),
                                    ("storageReady", healthy.ToString()),
                                    ("packageStorageReady", packageStorageReady.ToString()),
                                    ("workerEnabled", options.PackageWorkerEnabled.ToString()),
                                    ("instanceMode", instanceMode),
                                    ("signingConfigured", signingConfigured.ToString()),
                                    ("authMode", authMode)),
                                cancellationToken: cancellationToken);
                            return Results.Json(new {
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
                                dependencies = new {
                                    storageProvider = options.Provider,
                                    storageReady = healthy,
                                    packageStorageConfigured,
                                    packageStorageReady,
                                    workerEnabled = options.PackageWorkerEnabled,
                                    instanceMode,
                                    signingConfigured,
                                    authMode,
                                    oidc
                                }
                            }, statusCode: StatusCodes.Status503ServiceUnavailable);
                        }

                        return Results.Ok(new {
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
                            dependencies = new {
                                storageProvider = options.Provider,
                                storageReady = true,
                                packageStorageConfigured,
                                packageStorageReady = true,
                                workerEnabled = options.PackageWorkerEnabled,
                                instanceMode,
                                signingConfigured,
                                authMode,
                                oidc
                            }
                        });
                    } catch (Exception exception) {
                        var authMode = "unknown";
                        var packageStorageReady = false;
                        var oidc = VerifierServerHelpers.DescribeOidcStatus(authOptions.Oidc);
                        try {
                            authMode = await VerifierServerHelpers.ResolveAuthModeAsync(apiKeyStore,
                                configuredApiKeys.Count > 0, cancellationToken);
                            packageStorageReady = await blobStore.CheckHealthAsync(cancellationToken);
                        } catch {
                            // Keep the readiness response secret-safe even when auth storage is unavailable.
                        }

                        await VerifierAuditHelpers.WriteAuditEventAsync(
                            context.RequestServices.GetRequiredService<IVerifierAuditStore>(),
                            auditLogger,
                            context,
                            eventType: "readiness.failed",
                            result: "Exception",
                            metadata: VerifierAuditHelpers.CreateMetadata(
                                ("storageProvider", options.Provider),
                                ("packageStorageReady", packageStorageReady.ToString()),
                                ("signingConfigured", signingConfigured.ToString()),
                                ("authMode", authMode),
                                ("exceptionType", exception.GetType().Name)),
                            cancellationToken: cancellationToken);
                        return Results.Json(new {
                            status = "Unhealthy",
                            storage = options.Provider,
                            storageProvider = options.Provider,
                            packageStorageProvider,
                            packageStorageConfigured,
                            workerEnabled = options.PackageWorkerEnabled,
                            instanceMode,
                            applyMigrationsOnStartup = options.ApplyMigrationsOnStartup,
                            signing = signingConfigured ? "Enabled" : "Disabled",
                            warnings = VerifierServerHelpers.BuildDeploymentWarnings(options, packageStorageReady),
                            dependencies = new {
                                storageProvider = options.Provider,
                                storageReady = false,
                                packageStorageConfigured,
                                packageStorageReady,
                                workerEnabled = options.PackageWorkerEnabled,
                                instanceMode,
                                signingConfigured,
                                authMode,
                                oidc
                            }
                        },
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                })
            .RequireRateLimiting(VerifierRateLimitingRegistration.PublicPolicy);

        app.MapGet("/diagnostics/summary", async (HttpContext context, IVerifierAuditStore auditStore,
            IVerifierApiKeyStore apiKeyStore,
            IPackageVerificationJobStore jobStore, IPackageBlobStore blobStore, VerifierStorageOptions options,
            VerifierSecurityOptions securityOptions, VerifierAuthOptions authOptions,
            IConfiguration configuration, IWebHostEnvironment environment, CancellationToken cancellationToken) => {
                var envString = configuration["VerifierEnvironment"] ?? environment.EnvironmentName;
                if (!Enum.TryParse<VerifierEnvironmentMode>(envString, true, out var envMode)) {
                    envMode = VerifierEnvironmentMode.Development;
                }

                var configuredApiKeys = VerifierAuthorizationHelpers.BuildConfiguredApiKeys(securityOptions);
                var summary = await auditStore.GetSummaryAsync(cancellationToken);
                var jobSummary = await jobStore.GetSummaryAsync(cancellationToken);
                var packageStorageReady = await blobStore.CheckHealthAsync(cancellationToken);
                var packageStorageConfigured = !string.IsNullOrWhiteSpace(options.LocalStoragePath);
                var packageStorageProvider = VerifierServerHelpers.DescribePackageStorageProvider(options);
                var instanceMode = VerifierServerHelpers.DescribeInstanceMode(options.PackageWorkerEnabled);
                var warnings = VerifierServerHelpers.BuildDeploymentWarnings(options, packageStorageReady);

                int? packageBlobCount = null;
                if (packageStorageReady && !string.IsNullOrWhiteSpace(options.LocalStoragePath)
                                        && Directory.Exists(options.LocalStoragePath)) {
                    try {
                        packageBlobCount = Directory.GetFiles(options.LocalStoragePath, "*.owspkg").Length;
                    } catch {
                        // Non-fatal: leave null if enumeration fails (e.g. permission issue mid-request)
                    }
                }

                var signingKeyFingerprint = ReceiptChainVerifier.ComputeFingerprint(options.ReceiptSigningKey);
                var signingKeyFingerprintPresent = !string.IsNullOrWhiteSpace(signingKeyFingerprint);

                return Results.Ok(new {
                    environment = envMode.ToString(),
                    instanceMode,
                    workerEnabled = options.PackageWorkerEnabled,
                    storageProvider = options.Provider,
                    packageStorageProvider,
                    packageStorageConfigured,
                    packageStorageReady,
                    packageBlobCount,
                    applyMigrationsOnStartup = options.ApplyMigrationsOnStartup,
                    warnings,
                    signingKeyFingerprintPresent,
                    authMode = await VerifierServerHelpers.ResolveAuthModeAsync(apiKeyStore, configuredApiKeys.Count > 0,
                        cancellationToken),
                    oidc = VerifierServerHelpers.DescribeOidcStatus(authOptions.Oidc),
                    metrics = summary,
                    packageVerificationJobs = jobSummary
                });
            })
            .RequireRateLimiting(VerifierRateLimitingRegistration.DiagnosticsPolicy);
    }
}
