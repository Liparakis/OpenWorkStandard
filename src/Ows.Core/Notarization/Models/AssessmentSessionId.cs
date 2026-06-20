namespace Ows.Core.Notarization;

/// <summary>
/// Identifies an assessment session across checkpoints and receipts.
/// </summary>
/// <param name="Value">The stable session identifier value.</param>
public readonly record struct AssessmentSessionId(string Value)
{
    /// <summary>
    /// Creates a new assessment session identifier.
    /// </summary>
    /// <returns>A new session identifier.</returns>
    public static AssessmentSessionId Create() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Returns the underlying identifier value.
    /// </summary>
    /// <returns>The session identifier text.</returns>
    public override string ToString() => Value;
}