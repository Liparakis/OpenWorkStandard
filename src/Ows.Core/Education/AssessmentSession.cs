using Ows.Core.Notarization;
using Ows.Core.Verification;

namespace Ows.Core.Education;

/// <summary>
/// Links an educational assessment and student to a notarized verifier session and package validation outcome.
/// </summary>
public sealed record AssessmentSession {
    /// <summary>
    /// Gets the unique verifier assessment session identifier.
    /// </summary>
    public AssessmentSessionId Id { get; }

    /// <summary>
    /// Gets the associated educational assessment identifier.
    /// </summary>
    public AssessmentId AssessmentId { get; }

    /// <summary>
    /// Gets the student user identifier who owns the session.
    /// </summary>
    public UserId StudentUserId { get; }

    /// <summary>
    /// Gets the optional submission package identifier.
    /// </summary>
    public string? PackageId { get; }

    /// <summary>
    /// Gets the validation trust status of the session.
    /// </summary>
    public TrustStatus TrustStatus { get; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssessmentSession"/> class.
    /// </summary>
    public AssessmentSession(
        AssessmentSessionId id,
        AssessmentId assessmentId,
        UserId studentUserId,
        string? packageId,
        TrustStatus trustStatus,
        DateTimeOffset createdAt) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(assessmentId.Value, nameof(assessmentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(studentUserId.Value, nameof(studentUserId));
        if (createdAt == default) {
            throw new ArgumentException("CreatedAt must be a valid timestamp.", nameof(createdAt));
        }

        Id = id;
        AssessmentId = assessmentId;
        StudentUserId = studentUserId;
        PackageId = packageId;
        TrustStatus = trustStatus;
        CreatedAt = createdAt;
    }
}
