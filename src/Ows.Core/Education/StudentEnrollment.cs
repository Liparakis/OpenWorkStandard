namespace Ows.Core.Education;

/// <summary>
/// Links a student to a course offering.
/// </summary>
public sealed record StudentEnrollment
{
    /// <summary>
    /// Gets the unique student enrollment identifier.
    /// </summary>
    public EnrollmentId Id { get; }

    /// <summary>
    /// Gets the course offering identifier.
    /// </summary>
    public CourseOfferingId CourseOfferingId { get; }

    /// <summary>
    /// Gets the enrolled student identifier.
    /// </summary>
    public UserId StudentUserId { get; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StudentEnrollment"/> class.
    /// </summary>
    public StudentEnrollment(
        EnrollmentId id,
        CourseOfferingId courseOfferingId,
        UserId studentUserId,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(courseOfferingId.Value, nameof(courseOfferingId));
        ArgumentException.ThrowIfNullOrWhiteSpace(studentUserId.Value, nameof(studentUserId));
        if (createdAt == default)
        {
            throw new ArgumentException("CreatedAt must be a valid timestamp.", nameof(createdAt));
        }

        Id = id;
        CourseOfferingId = courseOfferingId;
        StudentUserId = studentUserId;
        CreatedAt = createdAt;
    }
}
