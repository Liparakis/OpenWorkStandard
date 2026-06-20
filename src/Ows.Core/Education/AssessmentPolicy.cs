namespace Ows.Core.Education;

/// <summary>
/// Dictates OWS verification rules and heartbeat tolerances for an assessment.
/// </summary>
public sealed record AssessmentPolicy
{
    /// <summary>
    /// Gets the unique policy identifier.
    /// </summary>
    public PolicyId Id { get; init; }

    /// <summary>
    /// Gets the institution identifier.
    /// </summary>
    public InstitutionId InstitutionId { get; init; }

    /// <summary>
    /// Gets the policy name.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Gets the target heartbeat interval in seconds.
    /// </summary>
    public int HeartbeatTargetSeconds { get; init; }

    /// <summary>
    /// Gets the heartbeat grace window in seconds.
    /// </summary>
    public int HeartbeatGraceSeconds { get; init; }

    /// <summary>
    /// Gets the threshold in seconds above which a lease gap is considered significant.
    /// </summary>
    public int SignificantGapSeconds { get; init; }

    /// <summary>
    /// Gets a value indicating whether verifier notarization receipts are strictly required.
    /// </summary>
    public bool RequireRemoteReceipts { get; init; }

    /// <summary>
    /// Gets a value indicating whether the timeline must anchor to a registered verifier session head.
    /// </summary>
    public bool RequirePackageAnchor { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssessmentPolicy"/> class.
    /// </summary>
    public AssessmentPolicy(
        PolicyId id,
        InstitutionId institutionId,
        string name,
        int heartbeatTargetSeconds,
        int heartbeatGraceSeconds,
        int significantGapSeconds,
        bool requireRemoteReceipts,
        bool requirePackageAnchor,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionId.Value, nameof(institutionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (heartbeatTargetSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatTargetSeconds), "Target seconds must be non-negative.");
        }
        if (heartbeatGraceSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatGraceSeconds), "Grace seconds must be non-negative.");
        }
        if (significantGapSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(significantGapSeconds), "Significant gap seconds must be non-negative.");
        }
        if (createdAt == default)
        {
            throw new ArgumentException("CreatedAt must be a valid timestamp.", nameof(createdAt));
        }

        Id = id;
        InstitutionId = institutionId;
        Name = name;
        HeartbeatTargetSeconds = heartbeatTargetSeconds;
        HeartbeatGraceSeconds = heartbeatGraceSeconds;
        SignificantGapSeconds = significantGapSeconds;
        RequireRemoteReceipts = requireRemoteReceipts;
        RequirePackageAnchor = requirePackageAnchor;
        CreatedAt = createdAt;
    }
}
