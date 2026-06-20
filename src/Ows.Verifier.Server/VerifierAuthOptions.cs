namespace Ows.Verifier.Server;

/// <summary>
/// Configures optional verifier authentication extensions beyond API keys.
/// </summary>
public sealed record VerifierAuthOptions
{
    /// <summary>
    /// Gets the optional OIDC/JWT bearer settings.
    /// </summary>
    public VerifierOidcOptions Oidc { get; init; } = new();
}

/// <summary>
/// Configures optional OIDC/JWT bearer validation for future human-facing access.
/// </summary>
public sealed record VerifierOidcOptions
{
    /// <summary>
    /// Gets a value indicating whether bearer authentication is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Gets the OIDC authority/issuer metadata address root.
    /// </summary>
    public string Authority { get; init; } = string.Empty;

    /// <summary>
    /// Gets the expected JWT audience.
    /// </summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Gets the configured client identifier for future interactive flows.
    /// </summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional client secret for future interactive flows.
    /// </summary>
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether discovery metadata must use HTTPS.
    /// </summary>
    public bool RequireHttpsMetadata { get; init; } = true;

    /// <summary>
    /// Gets the claim carrying the OWS role.
    /// </summary>
    public string RoleClaim { get; init; } = "role";

    /// <summary>
    /// Gets the claim carrying the institution scope.
    /// </summary>
    public string InstitutionClaim { get; init; } = "institution";

    /// <summary>
    /// Gets the claim carrying the human or student identity.
    /// </summary>
    public string UserIdClaim { get; init; } = "sub";

    /// <summary>
    /// Gets the claim carrying the email address for future dashboard context.
    /// </summary>
    public string EmailClaim { get; init; } = "email";

    /// <summary>
    /// Gets the claim carrying the display name for future dashboard context.
    /// </summary>
    public string DisplayNameClaim { get; init; } = "name";
}

/// <summary>
/// Exposes a safe summary of OIDC/JWT bearer configuration.
/// </summary>
public sealed record VerifierOidcStatus
{
    /// <summary>Gets a value indicating whether OIDC/JWT bearer auth is enabled.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets a value indicating whether an authority value is configured.</summary>
    public bool AuthorityConfigured { get; init; }

    /// <summary>Gets a value indicating whether an audience value is configured.</summary>
    public bool AudienceConfigured { get; init; }

    /// <summary>Gets a value indicating whether a role claim name is configured.</summary>
    public bool RoleClaimConfigured { get; init; }
}
