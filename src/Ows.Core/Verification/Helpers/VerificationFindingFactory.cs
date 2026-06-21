namespace Ows.Core.Verification;

/// <summary>
/// Factory for standard verification finding templates used throughout OWS verification reporting.
/// </summary>
internal static class VerificationFindingFactory {
    /// <summary>
    /// Finding template for indicating a complete, unbroken, and valid local timeline event chain.
    /// </summary>
    public static readonly VerificationFinding TimelineChainValidFinding = new() {
        Code = "timeline.chain.valid",
        Severity = "Low",
        Title = "Timeline chain valid",
        Detail = "The local timeline event chain is complete and unbroken.",
        TechnicalDetail = "All parent-child event hashes match and form a single continuous timeline.",
        ReviewerAction = "No action required."
    };

    /// <summary>
    /// Finding template for indicating a broken or inconsistent local timeline event chain.
    /// </summary>
    public static readonly VerificationFinding TimelineChainBrokenFinding = new() {
        Code = "timeline.chain.broken",
        Severity = "Critical",
        Title = "Timeline chain broken",
        Detail = "The local timeline event chain is broken or inconsistent.",
        TechnicalDetail = "A parent event hash mismatch was detected, or events were reordered or modified.",
        ReviewerAction = "Request a resubmission. The evidence chain is incomplete."
    };

    /// <summary>
    /// Finding template for indicating valid remote notarization receipts within the package.
    /// </summary>
    public static readonly VerificationFinding ReceiptChainValidFinding = new() {
        Code = "receipt.chain.valid",
        Severity = "Low",
        Title = "Receipt chain valid",
        Detail = "The package contains valid remote notarization receipts.",
        TechnicalDetail = "All checkpoints are signed and align with the local timeline.",
        ReviewerAction = "No action required."
    };

    /// <summary>
    /// Finding template for indicating that the package does not contain verifier receipts.
    /// </summary>
    public static readonly VerificationFinding ReceiptChainMissingFinding = new() {
        Code = "receipt.chain.missing",
        Severity = "Medium",
        Title = "Receipt chain missing",
        Detail = "The package does not contain verifier receipts.",
        TechnicalDetail = "No receipts were found in receipts.json, or remote verifier did not return receipts.",
        ReviewerAction =
            "Verify whether this package was intended to run in local-only mode. If remote verification is expected, request a resubmission."
    };

    /// <summary>
    /// Finding template for indicating a short session continuity gap that mostly preserves sequence flow.
    /// </summary>
    public static readonly VerificationFinding LeaseGapShortFinding = new() {
        Code = "lease.gap.short",
        Severity = "Warning",
        Title = "Short session continuity gap",
        Detail = "Session heartbeat was briefly interrupted, but session continuity was mostly preserved.",
        TechnicalDetail =
            "Session lease expired with a max gap duration less than or equal to the significance threshold.",
        ReviewerAction = "Review file changes around this interval. Session continuity could not be verified."
    };

    /// <summary>
    /// Finding template for indicating a significant session continuity gap where heartbeat was interrupted.
    /// </summary>
    public static readonly VerificationFinding LeaseGapLongFinding = new() {
        Code = "lease.gap.long",
        Severity = "High",
        Title = "Significant session continuity gap",
        Detail = "Session heartbeat was interrupted.",
        TechnicalDetail = "Session lease expired with a max gap duration exceeding the significance threshold.",
        ReviewerAction = "Review file changes around this interval. Session continuity could not be verified."
    };

    /// <summary>
    /// Finding template for indicating a package anchored to the registered verifier session head.
    /// </summary>
    public static readonly VerificationFinding PackageAnchorValidFinding = new() {
        Code = "package.anchor.valid",
        Severity = "Low",
        Title = "Package anchor valid",
        Detail = "The package is anchored to the registered verifier session head.",
        TechnicalDetail = "Timeline head matches the verifier session head.",
        ReviewerAction = "No action required."
    };

    /// <summary>
    /// Finding template for indicating a package that is not anchored to a registered verifier session head.
    /// </summary>
    public static readonly VerificationFinding PackageAnchorMissingFinding = new() {
        Code = "package.anchor.missing",
        Severity = "Medium",
        Title = "Package anchor missing",
        Detail = "The package is not anchored to a registered verifier session head.",
        TechnicalDetail = "The verifier session was not found or has no matching anchor.",
        ReviewerAction = "Ensure that the session was synchronized with the verifier."
    };

    /// <summary>
    /// Finding template for indicating that package files have modified hashes that do not match the manifest.
    /// </summary>
    public static readonly VerificationFinding PackageHashInvalidFinding = new() {
        Code = "package.hash.invalid",
        Severity = "Critical",
        Title = "Package hash invalid",
        Detail = "Package files have modified hashes that do not match the manifest.",
        TechnicalDetail = "SHA-256 hash of one or more files in the package does not match the manifest value.",
        ReviewerAction = "Reject the package as corrupted or modified. Request a resubmission."
    };

    /// <summary>
    /// Finding template for indicating that the session head reported by the verifier does not match the local timeline head.
    /// </summary>
    public static readonly VerificationFinding VerifierSessionHeadMismatchFinding = new() {
        Code = "verifier.session.head.mismatch",
        Severity = "High",
        Title = "Verifier session head mismatch",
        Detail = "The session head reported by the verifier does not match the local timeline head.",
        TechnicalDetail = "Mismatch between trusted remote session head hash and local package timeline head hash.",
        ReviewerAction = "Manual review recommended. Inspect timeline synchronization logs."
    };

    /// <summary>
    /// Finding template for indicating timeline events recorded after the remote verifier session lease expired.
    /// </summary>
    public static readonly VerificationFinding LeaseWorkAfterExpirationFinding = new() {
        Code = "lease.work_after_expiration",
        Severity = "High",
        Title = "Work after lease expiration",
        Detail = "Timeline events were recorded after the remote verifier session lease expired.",
        TechnicalDetail = "Local timeline events have timestamps after the lease expiration timestamp.",
        ReviewerAction = "Examine work recorded after lease expiration. Session continuity could not be verified."
    };

    /// <summary>
    /// Finding template for indicating that OWS was not observing the project for an interval of time.
    /// </summary>
    public static readonly VerificationFinding ObservationGapFinding = new() {
        Code = "observation.gap",
        Severity = "Low",
        Title = "Observation gap detected",
        Detail = "OWS was not observing the project for an interval of time.",
        TechnicalDetail = "Timeline contains an ObservationGapDetected event indicating the watcher was not running.",
        ReviewerAction =
            "Manual review recommended. Event presence is evidence of recorded activity. Event absence is not proof of misconduct."
    };

    /// <summary>
    /// Finding template for indicating a large file change that occurred during an unobserved gap.
    /// </summary>
    public static readonly VerificationFinding LargeUnobservedChangeFinding = new() {
        Code = "observation.large_unobserved_change",
        Severity = "High",
        Title = "Large unobserved change",
        Detail = "A large file change appeared while OWS was not observing this project.",
        TechnicalDetail =
            "Timeline contains a LargeUnobservedChangeDetected event exceeding byte or line thresholds during an unobserved gap.",
        ReviewerAction =
            "OWS was not observing this project during the interval below. During that interval, a large file change appeared. OWS can verify the current package hashes, but cannot verify the unobserved edit process. OWS cannot determine the cause. This is not proof of misconduct. Reviewers should ask the student to explain this interval."
    };

    /// <summary>
    /// Finding template for indicating a standard file change that occurred during an unobserved gap.
    /// </summary>
    public static readonly VerificationFinding UnobservedChangeFinding = new() {
        Code = "observation.unobserved_change",
        Severity = "Medium",
        Title = "Unobserved file change",
        Detail = "A file change appeared while OWS was not observing this project.",
        TechnicalDetail = "Timeline contains an UnobservedChangeDetected event during an observation gap.",
        ReviewerAction =
            "OWS was not observing this project during the interval below. During that interval, file changes appeared. OWS can verify the current package hashes, but cannot verify the unobserved edit process. This is not proof of misconduct. Reviewers should ask the student to explain this interval."
    };

    /// <summary>
    /// Finding template for indicating OWS could not verify that the local recovery snapshot matched the last committed snapshot state.
    /// </summary>
    public static readonly VerificationFinding SnapshotMismatchFinding = new() {
        Code = "observation.snapshot_mismatch",
        Severity = "High",
        Title = "Recovery snapshot mismatch",
        Detail = "OWS could not verify that the local recovery snapshot matched the last committed snapshot state.",
        TechnicalDetail =
            "The local observed_snapshot.json state did not match the last SnapshotUpdated commitment, or the snapshot baseline could not be trusted during recovery.",
        ReviewerAction =
            "OWS could not verify that the local recovery snapshot matched the last committed snapshot state. OWS cannot safely reconstruct the exact unobserved file delta for this interval. This is not proof of misconduct. Reviewers should ask the student to explain this interval if relevant."
    };

    /// <summary>
    /// Finding template for indicating a recovery snapshot that was not committed into the timeline.
    /// </summary>
    public static readonly VerificationFinding SnapshotUnboundFinding = new() {
        Code = "observation.snapshot_unbound",
        Severity = "Medium",
        Title = "Recovery snapshot unbound",
        Detail = "OWS found a recovery snapshot that was not committed into the timeline.",
        TechnicalDetail =
            "The local observed_snapshot.json existed without a matching SnapshotUpdated commitment, so OWS treated the baseline as legacy or untrusted for continuity reconstruction.",
        ReviewerAction =
            "OWS could not verify that the local recovery snapshot matched the last committed snapshot state. OWS cannot safely reconstruct the exact unobserved file delta for this interval. This is not proof of misconduct. Reviewers should ask the student to explain this interval if relevant."
    };
}
