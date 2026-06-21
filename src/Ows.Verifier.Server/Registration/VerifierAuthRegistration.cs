using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Ows.Verifier.Server;

/// <summary>
/// Handles registering verifier security configurations and authentication/authorization schemas in the DI container.
/// </summary>
internal static class VerifierAuthRegistration {
    /// <summary>
    /// Registers security/auth configurations and sets up JWT/OIDC authentication middleware dependencies.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="securityOptions">The security options.</param>
    /// <param name="authOptions">The authentication options.</param>
    /// <returns>The modified service collection.</returns>
    public static void AddVerifierAuth(this IServiceCollection services,
        VerifierSecurityOptions securityOptions,
        VerifierAuthOptions authOptions) {
        services.AddSingleton(securityOptions);
        services.AddSingleton(authOptions);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => VerifierServerHelpers.ConfigureOidcJwtBearer(options, authOptions.Oidc));

        services.AddAuthorization();
    }
}
