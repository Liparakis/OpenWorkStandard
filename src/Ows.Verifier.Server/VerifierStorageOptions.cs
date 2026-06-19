namespace Ows.Verifier.Server;

/// <summary>
/// Configures which verifier storage provider the server uses.
/// </summary>
public sealed record VerifierStorageOptions
{
    /// <summary>
    /// Gets the storage provider name. Supported today: <c>json</c> and <c>postgres</c>.
    /// </summary>
    public string Provider { get; init; } = "json";

    /// <summary>
    /// Gets the local JSON snapshot path used by the development storage backend.
    /// </summary>
    public string JsonStorePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the PostgreSQL connection string used by the durable backend.
    /// </summary>
    public string PostgresConnectionString { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional verifier receipt signing key.
    /// </summary>
    public string ReceiptSigningKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets the local filesystem directory where uploaded package blobs are saved.
    /// </summary>
    public string LocalStoragePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the maximum allowed uploaded package size in bytes.
    /// </summary>
    public long MaxPackageSizeBytes { get; init; } = 52428800; // 50 MB
}
