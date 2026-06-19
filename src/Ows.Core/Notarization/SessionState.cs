namespace Ows.Core.Notarization;

/// <summary>
/// Represents the locally persisted assessment session state shared across CLI, packaging, and verification.
/// </summary>
public sealed record SessionState
{
    /// <summary>
    /// Gets the active assessment session identifier.
    /// </summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional verifier base URL associated with the session.
    /// </summary>
    public string? VerifierUrl { get; init; }
}