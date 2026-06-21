namespace Ows.Verifier.Server;

/// <summary>
/// Configures the verifier's built-in HTTP rate limiting policies.
/// </summary>
public sealed record VerifierRateLimitingOptions {
    /// <summary>
    /// Gets a value indicating whether rate limiting is enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the public probe requests allowed per minute per client.
    /// </summary>
    public int PublicPermitLimit { get; init; } = 60;

    /// <summary>
    /// Gets the auth management requests allowed per minute per client.
    /// </summary>
    public int AuthPermitLimit { get; init; } = 10;

    /// <summary>
    /// Gets the upload requests allowed per minute per client.
    /// </summary>
    public int UploadPermitLimit { get; init; } = 6;

    /// <summary>
    /// Gets the session write requests allowed per minute per client.
    /// </summary>
    public int SessionWritePermitLimit { get; init; } = 30;

    /// <summary>
    /// Gets the authenticated read requests allowed per minute per client.
    /// </summary>
    public int ReadPermitLimit { get; init; } = 120;

    /// <summary>
    /// Gets the diagnostics and audit requests allowed per minute per client.
    /// </summary>
    public int DiagnosticsPermitLimit { get; init; } = 30;

    /// <summary>
    /// Gets the request queue depth for each limiter partition.
    /// </summary>
    public int QueueLimit { get; init; } = 0;
}
