namespace Ows.Core.Notarization;

/// <summary>
/// Represents the locally persisted assessment session state shared across CLI, packaging, and verification.
/// </summary>
public sealed record SessionState
{
    /// <summary>
    /// Gets the active assessment session identifier.
    /// </summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional verifier base URL associated with the session.
    /// </summary>
    public string? VerifierUrl { get; init; }

    /// <summary>
    /// Gets the optional institution identifier.
    /// </summary>
    public string? InstitutionId { get; init; }

    /// <summary>
    /// Gets the optional assessment identifier.
    /// </summary>
    public string? AssessmentId { get; init; }

    /// <summary>
    /// Gets the optional student user identifier.
    /// </summary>
    public string? StudentUserId { get; init; }

    /// <summary>
    /// Gets the optional course offering identifier.
    /// </summary>
    public string? CourseOfferingId { get; init; }

    /// <summary>
    /// Gets the last checkpoint timestamp.
    /// </summary>
    public DateTimeOffset? LastCheckpointAt { get; init; }

    /// <summary>
    /// Gets the last heartbeat timestamp.
    /// </summary>
    public DateTimeOffset? LastHeartbeatAt { get; init; }

    /// <summary>
    /// Gets the last package submission ID.
    /// </summary>
    public string? LastPackageId { get; init; }

    /// <summary>
    /// Gets whether the verifier is offline/unreachable.
    /// </summary>
    public bool IsVerifierOffline { get; init; }

    /// <summary>
    /// Gets whether the heartbeat is failing.
    /// </summary>
    public bool IsHeartbeatFailing { get; init; }

    /// <summary>
    /// Gets whether the session is degraded (lease gap).
    /// </summary>
    public bool IsDegraded { get; init; }

    /// <summary>
    /// Gets the last heartbeat error message.
    /// </summary>
    public string? LastHeartbeatError { get; init; }
}