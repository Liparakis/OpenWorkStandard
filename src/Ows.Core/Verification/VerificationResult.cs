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
    /// Gets the list of verifier key fingerprints that signed the receipts in the package.
    /// </summary>
    public IReadOnlyList<string> VerifiedKeyFingerprints { get; init; } = [];

    /// <summary>
    /// Creates a successful verification result.
    /// </summary>
    /// <param name="summary">The result summary.</param>
    /// <param name="trustStatus">The trust grade assigned to the result.</param>
    /// <param name="findings">Optional verification findings.</param>
    /// <param name="verifiedKeyFingerprints">Optional list of verified key fingerprints.</param>
    /// <returns>A successful verification result.</returns>
    public static VerificationResult Success(
        string summary,
        TrustStatus trustStatus = TrustStatus.Verified,
        IReadOnlyList<VerificationFinding>? findings = null,
        IReadOnlyList<string>? verifiedKeyFingerprints = null) =>
        new() {
            IsSuccess = true,
            TrustStatus = trustStatus,
            Summary = summary,
            Findings = findings ?? [],
            VerifiedKeyFingerprints = verifiedKeyFingerprints ?? []
        };

    /// <summary>
    /// Creates a failed verification result.
    /// </summary>
    /// <param name="summary">The result summary.</param>
    /// <param name="errors">The validation or verification errors.</param>
    /// <param name="findings">Optional verification findings.</param>
    /// <param name="verifiedKeyFingerprints">Optional list of verified key fingerprints.</param>
    /// <returns>A failed verification result.</returns>
    public static VerificationResult Failure(
        string summary,
        IReadOnlyList<string>? errors = null,
        IReadOnlyList<VerificationFinding>? findings = null,
        IReadOnlyList<string>? verifiedKeyFingerprints = null) =>
        new() {
            IsSuccess = false,
            TrustStatus = TrustStatus.Invalid,
            Summary = summary,
            Errors = errors ?? [],
            Findings = findings ?? [],
            VerifiedKeyFingerprints = verifiedKeyFingerprints ?? []
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

    /// <summary>
    /// Gets the remote receipt alignment metadata.
    /// </summary>
    public ReportReceiptsInfo Receipts { get; init; } = new();

    /// <summary>
    /// Gets the session lease continuity metadata.
    /// </summary>
    public ReportLeaseInfo Lease { get; init; } = new();

    /// <summary>
    /// Gets the package anchor status metadata.
    /// </summary>
    public ReportAnchorInfo Anchor { get; init; } = new();

    /// <summary>
    /// Gets the optional educational context metadata.
    /// </summary>
    public ReportEducationContext? Education { get; init; }
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
    /// Gets the session ID.
    /// </summary>
    public string SessionId { get; init; } = string.Empty;
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

/// <summary>
/// Structured remote receipt alignment info for verification reports.
/// </summary>
public sealed record ReportReceiptsInfo {
    /// <summary>
    /// Gets the alignment status (e.g. Aligned, Misaligned, Missing).
    /// </summary>
    public string Alignment { get; init; } = string.Empty;

    /// <summary>
    /// Gets the total number of receipts.
    /// </summary>
    public int ReceiptCount { get; init; }

    /// <summary>
    /// Gets the head receipt hash.
    /// </summary>
    public string HeadReceiptHash { get; init; } = string.Empty;
}

/// <summary>
/// Structured session lease gap info.
/// </summary>
public sealed record ReportLeaseGapInfo {
    /// <summary>
    /// Gets the UTC start timestamp of the gap.
    /// </summary>
    public DateTimeOffset StartTime { get; init; }

    /// <summary>
    /// Gets the duration of the gap in seconds.
    /// </summary>
    public int DurationSeconds { get; init; }
}

/// <summary>
/// Structured session lease continuity info.
/// </summary>
public sealed record ReportLeaseInfo {
    /// <summary>
    /// Gets the lease status (e.g. Active, Degraded, Unverified, None).
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Gets the last heartbeat timestamp.
    /// </summary>
    public string LastHeartbeatAt { get; init; } = "None";

    /// <summary>
    /// Gets the lease expiration timestamp.
    /// </summary>
    public string LeaseExpiresAt { get; init; } = "None";

    /// <summary>
    /// Gets the detected lease gaps.
    /// </summary>
    public IReadOnlyList<ReportLeaseGapInfo> Gaps { get; init; } = [];
}

/// <summary>
/// Structured package anchor status.
/// </summary>
public sealed record ReportAnchorInfo {
    /// <summary>
    /// Gets the anchor status (e.g. Anchored, Missing, Mismatch).
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Gets the timestamp when anchored.
    /// </summary>
    public string AnchoredAt { get; init; } = "None";

    /// <summary>
    /// Gets the anchored session head timeline hash.
    /// </summary>
    public string AnchoredSessionHead { get; init; } = "None";
}

/// <summary>
/// Structured educational context for verification reports.
/// </summary>
public sealed record ReportEducationContext {
    /// <summary>
    /// Gets the unique institution identifier.
    /// </summary>
    public string? InstitutionId { get; init; }

    /// <summary>
    /// Gets the name of the institution.
    /// </summary>
    public string? InstitutionName { get; init; }

    /// <summary>
    /// Gets the unique course identifier.
    /// </summary>
    public string? CourseId { get; init; }

    /// <summary>
    /// Gets the academic course code.
    /// </summary>
    public string? CourseCode { get; init; }

    /// <summary>
    /// Gets the course title.
    /// </summary>
    public string? CourseTitle { get; init; }

    /// <summary>
    /// Gets the unique class group identifier.
    /// </summary>
    public string? ClassGroupId { get; init; }

    /// <summary>
    /// Gets the class group cohort name.
    /// </summary>
    public string? ClassGroupName { get; init; }

    /// <summary>
    /// Gets the unique assessment identifier.
    /// </summary>
    public string? AssessmentId { get; init; }

    /// <summary>
    /// Gets the assessment title.
    /// </summary>
    public string? AssessmentTitle { get; init; }

    /// <summary>
    /// Gets the unique student user identifier.
    /// </summary>
    public string? StudentUserId { get; init; }

    /// <summary>
    /// Gets the student's display name.
    /// </summary>
    public string? StudentDisplayName { get; init; }

    /// <summary>
    /// Gets the student's external identifier.
    /// </summary>
    public string? StudentExternalId { get; init; }
}
