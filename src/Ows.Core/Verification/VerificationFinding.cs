namespace Ows.Core.Verification;

/// <summary>
/// Represents a concrete verification finding that affects trust interpretation.
/// </summary>
public sealed record VerificationFinding
{
    /// <summary>
    /// Gets the stable finding code.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Gets the short finding title.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets the explanatory detail for the finding.
    /// </summary>
    public string Detail { get; init; } = string.Empty;
}
