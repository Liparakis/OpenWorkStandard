namespace Ows.Core.Verification;

/// <summary>
/// Describes the inputs required to verify an OWS package.
/// </summary>
public sealed record PackageVerificationRequest {
    /// <summary>
    /// Gets the package path to verify.
    /// </summary>
    public string PackagePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional trusted receipt chain fetched from a live verifier.
    /// </summary>
    public Notarization.ReceiptChain? TrustedReceiptChain { get; init; }

    /// <summary>
    /// Gets the optional trusted session head fetched from a live verifier.
    /// </summary>
    public Notarization.SessionHeadResponse? TrustedSessionHead { get; init; }

    /// <summary>
    /// Gets the optional last heartbeat timestamp of the session in UTC.
    /// </summary>
    public DateTimeOffset? SessionLastHeartbeatAt { get; init; }

    /// <summary>
    /// Gets the optional lease expiration timestamp of the session in UTC.
    /// </summary>
    public DateTimeOffset? SessionLeaseExpiresAt { get; init; }

    /// <summary>
    /// Gets a value indicating whether a lease expiration gap occurred during this session.
    /// </summary>
    public bool SessionHasLeaseGap { get; init; }

    /// <summary>
    /// Gets the maximum lease expiration gap duration observed during this session, in seconds.
    /// </summary>
    public int SessionMaxLeaseGapSeconds { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when the first lease gap started.
    /// </summary>
    public DateTimeOffset? SessionFirstLeaseGapAt { get; init; }

    /// <summary>
    /// Gets the significant gap threshold in seconds. Gaps larger than this mark a package Unverified instead of Degraded.
    /// </summary>
    public int SignificantGapSeconds { get; init; } = 300;

    /// <summary>
    /// Gets the optional external context metadata for reporting and display.
    /// </summary>
    public ReportExternalContext? ExternalContext { get; init; }
}
