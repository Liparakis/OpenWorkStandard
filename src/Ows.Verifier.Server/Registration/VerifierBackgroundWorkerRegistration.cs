using Ows.Verifier.Server.Packages;

namespace Ows.Verifier.Server;

/// <summary>
/// Handles registering the in-process verifier package background verification worker into the DI container.
/// </summary>
internal static class VerifierBackgroundWorkerRegistration {
    /// <summary>
    /// Registers the <see cref="PackageVerificationWorker"/> hosted service if it is enabled.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="normalizedStorageOptions">The storage options.</param>
    /// <returns>The modified service collection.</returns>
    public static void AddVerifierBackgroundWorker(this IServiceCollection services,
        VerifierStorageOptions normalizedStorageOptions) {
        if (normalizedStorageOptions.PackageWorkerEnabled) {
            services.AddHostedService<PackageVerificationWorker>();
        }
    }
}
