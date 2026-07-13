namespace Ows.Core.Verification;

/// <summary>
/// Represents the outcome of verifying an OWS package or evidence store.
/// </summary>
public sealed record VerificationResult {
    /// <summary>
    /// Gets a value indicating whether the verification passed.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the trust grade assigned to the verification outcome.
    /// </summary>
    public TrustStatus TrustStatus { get; init; }

    /// <summary>
    /// Gets a summary suitable for CLI and report output.
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Gets any verification errors that prevented a clean result.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Gets concrete verification findings that explain the assigned trust grade.
    /// </summary>
    public IReadOnlyList<VerificationFinding> Findings { get; init; } = [];

    /// <summary>
    /// Gets the offline package signature state: Valid, Unsigned, or Invalid.
    /// </summary>
    public string SignatureStatus { get; init; } = "Unsigned";

    /// <summary>
    /// Creates a successful verification result.
    /// </summary>
    /// <param name="summary">The result summary.</param>
    /// <param name="trustStatus">The trust grade assigned to the result.</param>
    /// <param name="findings">Optional verification findings.</param>
    /// <returns>A successful verification result.</returns>
    public static VerificationResult Success(
        string summary,
        TrustStatus trustStatus = TrustStatus.Verified,
        IReadOnlyList<VerificationFinding>? findings = null) =>
        new() {
            IsSuccess = true,
            TrustStatus = trustStatus,
            Summary = summary,
            Findings = findings ?? []
        };

    /// <summary>
    /// Creates a failed verification result.
    /// </summary>
    /// <param name="summary">The result summary.</param>
    /// <param name="errors">The validation or verification errors.</param>
    /// <param name="findings">Optional verification findings.</param>
    /// <returns>A failed verification result.</returns>
    public static VerificationResult Failure(
        string summary,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<VerificationFinding>? findings = null) =>
        new() {
            IsSuccess = false,
            TrustStatus = TrustStatus.Invalid,
            Summary = summary,
            Errors = errors ?? [],
            Findings = findings ?? []
        };

    /// <summary>
    /// Gets the plain-English trust grade explanation.
    /// </summary>
    public string TrustExplanation { get; init; } = string.Empty;

    /// <summary>
    /// Gets the reviewer recommendation.
    /// </summary>
    public string Recommendation { get; init; } = string.Empty;

    /// <summary>
    /// Gets the timestamp when the verification was generated.
    /// </summary>
    public string GeneratedAt { get; init; } = string.Empty;

    /// <summary>
    /// Gets the package info metadata.
    /// </summary>
    public ReportPackageInfo Package { get; init; } = new();

    /// <summary>
    /// Gets the timeline integrity metadata.
    /// </summary>
    public ReportTimelineInfo Timeline { get; init; } = new();

}

/// <summary>
/// Structured package info for verification reports.
/// </summary>
public sealed record ReportPackageInfo {
    /// <summary>
    /// Gets the package ID.
    /// </summary>
    public string PackageId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the SHA-256 hash of the package.
    /// </summary>
    public string PackageHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the canonical logical package-root hash.
    /// </summary>
    public string PackageRootHash { get; init; } = string.Empty;

}

/// <summary>
/// Structured timeline integrity info for verification reports.
/// </summary>
public sealed record ReportTimelineInfo {
    /// <summary>
    /// Gets the timeline integrity status (e.g. Valid, Broken).
    /// </summary>
    public string Integrity { get; init; } = string.Empty;

    /// <summary>
    /// Gets the total number of events in the timeline.
    /// </summary>
    public int EventCount { get; init; }

    /// <summary>
    /// Gets the head event hash of the timeline.
    /// </summary>
    public string HeadEventHash { get; init; } = string.Empty;
}
