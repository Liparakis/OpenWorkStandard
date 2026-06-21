using Ows.Core.Reporting;
using Ows.Core.Verification;

namespace Ows.Verifier.Server;

/// <summary>
/// Handles registering verifier observability and report generation services into the DI container.
/// </summary>
internal static class VerifierObservabilityRegistration {
    /// <summary>
    /// Registers package verifier engine and report generator services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The modified service collection.</returns>
    public static void AddVerifierObservability(this IServiceCollection services) {
        services.AddSingleton<IPackageVerifier, OwsPackageVerifier>();
        services.AddSingleton<IReportGenerator, OwsReportGenerator>();
    }
}
