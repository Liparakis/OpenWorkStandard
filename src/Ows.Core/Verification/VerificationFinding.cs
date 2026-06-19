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
    /// Gets the finding severity level (e.g. Low, Medium, High, Critical, Info, Warning).
    /// </summary>
    public string Severity { get; init; } = "Low";

    /// <summary>
    /// Gets the short finding title.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets the explanatory detail for the finding.
    /// </summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>
    /// Gets the deep technical detail explaining why this finding was raised.
    /// </summary>
    public string TechnicalDetail { get; init; } = string.Empty;

    /// <summary>
    /// Gets the suggested action a reviewer or operator should take.
    /// </summary>
    public string ReviewerAction { get; init; } = string.Empty;
}