namespace Ows.Core.Verification.Helpers;

internal static class VerificationFindingFactory {
    public static readonly VerificationFinding TimelineChainValidFinding = new() {
        Code = "timeline.chain.valid",
        Severity = "Low",
        Title = "Timeline chain valid",
        Detail = "The local timeline event chain is complete and unbroken.",
        TechnicalDetail = "All parent-child event hashes match and form a single continuous timeline.",
        ReviewerAction = "No action required."
    };

    public static readonly VerificationFinding TimelineChainBrokenFinding = new() {
        Code = "timeline.chain.broken",
        Severity = "Critical",
        Title = "Timeline chain broken",
        Detail = "The local timeline event chain is broken or inconsistent.",
        TechnicalDetail = "A parent event hash mismatch was detected, or events were reordered or modified.",
        ReviewerAction = "Request a resubmission. The evidence chain is incomplete."
    };

    public static readonly VerificationFinding PackageHashInvalidFinding = new() {
        Code = "package.hash.invalid",
        Severity = "Critical",
        Title = "Package hash invalid",
        Detail = "Package files have modified hashes that do not match the manifest.",
        TechnicalDetail = "SHA-256 hash of one or more files in the package does not match the manifest value.",
        ReviewerAction = "Reject the package as corrupted or modified. Request a resubmission."
    };

    public static readonly VerificationFinding SnapshotMismatchFinding = new() {
        Code = "observation.snapshot_mismatch",
        Severity = "High",
        Title = "Recovery snapshot mismatch",
        Detail = "OWS could not verify that the local recovery snapshot matched the last committed snapshot state.",
        TechnicalDetail = "The recovery baseline could not be trusted during observation-gap reconstruction.",
        ReviewerAction = "Review the interval manually. This is not proof of misconduct."
    };

    public static readonly VerificationFinding SnapshotUnboundFinding = new() {
        Code = "observation.snapshot_unbound",
        Severity = "Medium",
        Title = "Recovery snapshot unbound",
        Detail = "OWS found a recovery snapshot that was not committed into the timeline.",
        TechnicalDetail = "The baseline was treated as unbound or untrusted for continuity reconstruction.",
        ReviewerAction = "Review the interval manually. This is not proof of misconduct."
    };
}
