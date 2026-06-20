namespace Ows.Verifier.Server;

/// <summary>
/// Configures the verifier's API access guard.
/// </summary>
public sealed record VerifierSecurityOptions
{
    /// <summary>
    /// Gets the optional legacy operator API key required for verifier requests.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
     /// Gets the request header name carrying the shared API key.
     /// </summary>
    public string HeaderName { get; init; } = "X-OWS-Verifier-Key";

    /// <summary>
    /// Gets the configured verifier API keys and their scopes.
    /// </summary>
    public IReadOnlyList<VerifierApiKeyOptions> ApiKeys { get; init; } = [];
}

/// <summary>
/// Describes one configured verifier API key and its access scope.
/// </summary>
public sealed record VerifierApiKeyOptions
{
    /// <summary>
    /// Gets the API key secret.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets the role granted by the key. Supported values are <c>operator</c> and <c>reviewer</c>.
    /// </summary>
    public string Role { get; init; } = "operator";

    /// <summary>
    /// Gets the optional institution scope. Reviewer keys must set this.
    /// </summary>
    public string? InstitutionId { get; init; }
}
