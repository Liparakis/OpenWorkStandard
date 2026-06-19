using System;

namespace Ows.Core.Education;

/// <summary>
/// Links a user to a course offering under a specific academic role.
/// </summary>
public sealed record Enrollment
{
    /// <summary>
    /// Gets the unique enrollment identifier.
    /// </summary>
    public EnrollmentId Id { get; init; }

    /// <summary>
    /// Gets the course offering identifier.
    /// </summary>
    public CourseOfferingId CourseOfferingId { get; init; }

    /// <summary>
    /// Gets the user identifier.
    /// </summary>
    public UserId UserId { get; init; }

    /// <summary>
    /// Gets the user's role inside this enrollment.
    /// </summary>
    public EducationRole Role { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Enrollment"/> class.
    /// </summary>
    public Enrollment(EnrollmentId id, CourseOfferingId courseOfferingId, UserId userId, EducationRole role, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(courseOfferingId.Value, nameof(courseOfferingId));
        ArgumentException.ThrowIfNullOrWhiteSpace(userId.Value, nameof(userId));
        if (createdAt == default)
        {
            throw new ArgumentException("CreatedAt must be a valid timestamp.", nameof(createdAt));
        }

        Id = id;
        CourseOfferingId = courseOfferingId;
        UserId = userId;
        Role = role;
        CreatedAt = createdAt;
    }
}
