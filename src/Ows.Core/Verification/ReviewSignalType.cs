namespace Ows.Core.Verification;

/// <summary>
/// Identifies neutral review indicators surfaced during verification.
/// </summary>
public enum ReviewSignalType
{
    /// <summary>
    /// A large insert was detected.
    /// </summary>
    LargeInsert,

    /// <summary>
    /// A gap in the recorded history was detected.
    /// </summary>
    MissingHistory,

    /// <summary>
    /// A timeline inconsistency was detected.
    /// </summary>
    TimelineAnomaly,

    /// <summary>
    /// A hash mismatch was detected.
    /// </summary>
    HashMismatch,

    /// <summary>
    /// A graph structure inconsistency was detected.
    /// </summary>
    BrokenGraph,

    /// <summary>
    /// The captured duration appears unusually short.
    /// </summary>
    ShortDuration,

    /// <summary>
    /// An uncategorized review signal was raised.
    /// </summary>
    Other
}
