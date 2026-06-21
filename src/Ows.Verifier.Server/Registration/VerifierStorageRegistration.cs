using Ows.Core.Education;
using Ows.Core.Notarization;

namespace Ows.Verifier.Server;

/// <summary>
/// Handles registering verifier storage components and data stores into the DI container.
/// </summary>
internal static class VerifierStorageRegistration {
    /// <summary>
    /// Registers all data stores, including API keys, package submissions, blobs, audit logs, and education stores, using Postgres or JSON providers.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="normalizedStorageOptions">The storage options.</param>
    /// <param name="environment">The hosting environment.</param>
    /// <returns>The modified service collection.</returns>
    public static void AddVerifierStorage(this IServiceCollection services,
        VerifierStorageOptions normalizedStorageOptions,
        IWebHostEnvironment environment) {
        services.AddSingleton(normalizedStorageOptions);

        services.AddSingleton<IVerifierApiKeyStore>(_ => {
            var storeRoot = Path.GetDirectoryName(normalizedStorageOptions.JsonStorePath) ??
                            environment.ContentRootPath;
            return string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
                ? new PostgresVerifierApiKeyStore(
                    !string.IsNullOrWhiteSpace(normalizedStorageOptions.PostgresConnectionString)
                        ? normalizedStorageOptions.PostgresConnectionString
                        : throw new InvalidOperationException(
                            "VerifierStorage:PostgresConnectionString must be configured when VerifierStorage:Provider=postgres."),
                    normalizedStorageOptions.ApplyMigrationsOnStartup)
                : new JsonFileVerifierApiKeyStore(Path.Combine(storeRoot, "api_keys.json"));
        });

        services.AddSingleton<IPackageSubmissionStore>(_ =>
            string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
                ? new PostgresPackageSubmissionStore(
                    normalizedStorageOptions.PostgresConnectionString,
                    normalizedStorageOptions.ApplyMigrationsOnStartup)
                : new JsonFilePackageSubmissionStore(Path.Combine(
                    Path.GetDirectoryName(normalizedStorageOptions.JsonStorePath) ?? environment.ContentRootPath,
                    "package_submissions.json")));

        services.AddSingleton<IPackageBlobStore>(_ =>
            new LocalFilePackageBlobStore(normalizedStorageOptions.LocalStoragePath,
                normalizedStorageOptions.MaxPackageSizeBytes));

        services.AddSingleton<IPackageVerificationJobStore>(_ => {
            var storeRoot = Path.GetDirectoryName(normalizedStorageOptions.JsonStorePath) ??
                            environment.ContentRootPath;
            return string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
                ? new PostgresPackageVerificationJobStore(
                    !string.IsNullOrWhiteSpace(normalizedStorageOptions.PostgresConnectionString)
                        ? normalizedStorageOptions.PostgresConnectionString
                        : throw new InvalidOperationException(
                            "VerifierStorage:PostgresConnectionString must be configured when VerifierStorage:Provider=postgres."),
                    normalizedStorageOptions.ApplyMigrationsOnStartup)
                : new JsonFilePackageVerificationJobStore(Path.Combine(storeRoot, "package_verification_jobs.json"));
        });

        services.AddSingleton<IVerifierAuditStore>(_ => {
            var storeRoot = Path.GetDirectoryName(normalizedStorageOptions.JsonStorePath) ??
                            environment.ContentRootPath;
            return string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
                ? new PostgresVerifierAuditStore(
                    !string.IsNullOrWhiteSpace(normalizedStorageOptions.PostgresConnectionString)
                        ? normalizedStorageOptions.PostgresConnectionString
                        : throw new InvalidOperationException(
                            "VerifierStorage:PostgresConnectionString must be configured when VerifierStorage:Provider=postgres."),
                    normalizedStorageOptions.ApplyMigrationsOnStartup)
                : new JsonFileVerifierAuditStore(Path.Combine(storeRoot, "audit_events.json"));
        });

        services.AddSingleton<IVerifierStorage>(_ => normalizedStorageOptions.Provider switch {
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
            _ => throw new NotSupportedException(
                $"Unsupported verifier storage provider: {normalizedStorageOptions.Provider}")
        });

        services.AddSingleton<IEducationStore>(_ => {
            var storePath = Path.Combine(
                Path.GetDirectoryName(normalizedStorageOptions.JsonStorePath) ?? environment.ContentRootPath,
                "education.json");
            return string.Equals(normalizedStorageOptions.Provider, "postgres", StringComparison.OrdinalIgnoreCase)
                   && !string.IsNullOrWhiteSpace(normalizedStorageOptions.PostgresConnectionString)
                ? new PostgresEducationStore(normalizedStorageOptions.PostgresConnectionString)
                : new JsonFileEducationStore(storePath);
        });
    }
}
