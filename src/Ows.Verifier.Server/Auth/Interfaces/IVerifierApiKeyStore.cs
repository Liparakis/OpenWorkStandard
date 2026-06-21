namespace Ows.Verifier.Server;

/// <summary>
/// Persists verifier API keys without storing raw key material.
/// </summary>
internal interface IVerifierApiKeyStore
{
    /// <summary>
    /// Initializes the backing store.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns whether any non-revoked, non-expired persisted keys exist.
    /// </summary>
    Task<bool> HasActiveKeysAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new persisted API key and returns the raw secret once.
    /// </summary>
    Task<VerifierApiKeyCreateResult> CreateAsync(
        VerifierApiKeyCreateRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists persisted API key metadata without revealing raw secrets.
    /// </summary>
    Task<IReadOnlyList<VerifierApiKeyMetadata>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Revokes a persisted API key.
    /// </summary>
    Task<bool> RevokeAsync(string keyId, CancellationToken cancellationToken);

    /// <summary>
    /// Authenticates a raw API key against persisted key records.
    /// </summary>
    Task<VerifierAccessContext?> AuthenticateAsync(string rawApiKey, CancellationToken cancellationToken);
}