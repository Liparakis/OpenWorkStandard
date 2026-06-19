namespace Ows.Core.Verification;

/// <summary>
/// Represents a neutral indicator that may justify additional human review.
/// </summary>
public sealed record ReviewSignal
{
    /// <summary>
    /// Gets the review signal category.
    /// </summary>
    public ReviewSignalType SignalType { get; init; }

    /// <summary>
    /// Gets the short title shown in reports.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets the explanatory detail for the signal.
    /// </summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>
    /// Gets a relative severity from 1 to 5.
    /// </summary>
    public int Severity { get; init; } = 1;
}