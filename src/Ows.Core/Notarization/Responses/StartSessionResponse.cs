namespace Ows.Core.Notarization;

/// <summary>
/// Represents the minimal response body returned when a verifier session starts.
/// </summary>
public sealed record StartSessionResponse {
    /// <summary>
    /// Gets the started assessment session identifier.
    /// </summary>
    public string SessionId { get; init; } = string.Empty;
}
