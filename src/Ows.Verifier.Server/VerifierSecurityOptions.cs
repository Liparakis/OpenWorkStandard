namespace Ows.Verifier.Server;

/// <summary>
/// Configures the verifier's minimal API access guard.
/// </summary>
public sealed record VerifierSecurityOptions
{
    /// <summary>
    /// Gets the optional shared API key required for verifier requests.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets the request header name carrying the shared API key.
    /// </summary>
    public string HeaderName { get; init; } = "X-OWS-Verifier-Key";
}
