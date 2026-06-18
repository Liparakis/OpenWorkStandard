namespace Ows.Core.Verification;

/// <summary>
/// Represents the outcome of verifying an OWS package or evidence store.
/// </summary>
public sealed record VerificationResult
{
    /// <summary>
    /// Gets a value indicating whether the verification passed.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Gets a summary suitable for CLI and report output.
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Gets any verification errors that prevented a clean result.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the review signals raised during verification.
    /// </summary>
    public IReadOnlyList<ReviewSignal> ReviewSignals { get; init; } = Array.Empty<ReviewSignal>();

    /// <summary>
    /// Creates a successful verification result.
    /// </summary>
    /// <param name="summary">The result summary.</param>
    /// <param name="reviewSignals">Optional review signals.</param>
    /// <returns>A successful verification result.</returns>
    public static VerificationResult Success(string summary, IReadOnlyList<ReviewSignal>? reviewSignals = null) =>
        new()
        {
            IsSuccess = true,
            Summary = summary,
            ReviewSignals = reviewSignals ?? Array.Empty<ReviewSignal>()
        };

    /// <summary>
    /// Creates a failed verification result.
    /// </summary>
    /// <param name="summary">The result summary.</param>
    /// <param name="errors">The validation or verification errors.</param>
    /// <param name="reviewSignals">Optional review signals.</param>
    /// <returns>A failed verification result.</returns>
    public static VerificationResult Failure(
        string summary,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<ReviewSignal>? reviewSignals = null) =>
        new()
        {
            IsSuccess = false,
            Summary = summary,
            Errors = errors ?? Array.Empty<string>(),
            ReviewSignals = reviewSignals ?? Array.Empty<ReviewSignal>()
        };
}
