namespace Ows.Verifier.Server;

/// <summary>
/// Provides extension methods on <see cref="IEndpointRouteBuilder"/> to register all OWS Verifier endpoints.
/// </summary>
public static class VerifierEndpointRouteBuilderExtensions {
    /// <summary>
    /// Maps all OWS Verifier endpoints to the application pipeline routing.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The modified endpoint route builder.</returns>
    public static void MapVerifierEndpoints(this IEndpointRouteBuilder app) {
        app.MapVerifierAuthEndpoints();
        app.MapVerifierSessionEndpoints();
        app.MapVerifierPackageEndpoints();
        app.MapVerifierAuditEndpoints();
        app.MapVerifierDiagnosticsEndpoints();
        app.MapVerifierMetricsEndpoints();
    }
}
