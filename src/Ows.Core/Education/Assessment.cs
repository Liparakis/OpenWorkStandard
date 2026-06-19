using System;

namespace Ows.Core.Education;

/// <summary>
/// Represents an assignment, exam, or project in a course offering.
/// </summary>
public sealed record Assessment
{
    /// <summary>
    /// Gets the unique assessment identifier.
    /// </summary>
    public AssessmentId Id { get; init; }

    /// <summary>
    /// Gets the institution identifier.
    /// </summary>
    public InstitutionId InstitutionId { get; init; }

    /// <summary>
    /// Gets the course offering identifier this assessment is under.
    /// </summary>
    public CourseOfferingId CourseOfferingId { get; init; }

    /// <summary>
    /// Gets the title of the assessment.
    /// </summary>
    public string Title { get; init; }

    /// <summary>
    /// Gets the optional UTC start window timestamp.
    /// </summary>
    public DateTimeOffset? StartsAt { get; init; }

    /// <summary>
    /// Gets the optional UTC end window (deadline) timestamp.
    /// </summary>
    public DateTimeOffset? EndsAt { get; init; }

    /// <summary>
    /// Gets the optional validation policy identifier to apply.
    /// </summary>
    public PolicyId? PolicyId { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Assessment"/> class.
    /// </summary>
    public Assessment(AssessmentId id, InstitutionId institutionId, CourseOfferingId courseOfferingId, string title, DateTimeOffset? startsAt, DateTimeOffset? endsAt, PolicyId? policyId, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionId.Value, nameof(institutionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(courseOfferingId.Value, nameof(courseOfferingId));
        ArgumentException.ThrowIfNullOrWhiteSpace(title, nameof(title));
        if (createdAt == default)
        {
            throw new ArgumentException("CreatedAt must be a valid timestamp.", nameof(createdAt));
        }

        Id = id;
        InstitutionId = institutionId;
        CourseOfferingId = courseOfferingId;
        Title = title;
        StartsAt = startsAt;
        EndsAt = endsAt;
        PolicyId = policyId;
        CreatedAt = createdAt;
    }
}
