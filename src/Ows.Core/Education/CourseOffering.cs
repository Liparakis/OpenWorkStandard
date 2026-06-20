namespace Ows.Core.Education;

/// <summary>
/// Represents a specific delivery of a course to a class cohort in a term.
/// </summary>
public sealed record CourseOffering
{
    /// <summary>
    /// Gets the unique course offering identifier.
    /// </summary>
    public CourseOfferingId Id { get; }

    /// <summary>
    /// Gets the institution identifier.
    /// </summary>
    public InstitutionId InstitutionId { get; }

    /// <summary>
    /// Gets the course template identifier.
    /// </summary>
    public CourseId CourseId { get; }

    /// <summary>
    /// Gets the class group cohort identifier.
    /// </summary>
    public ClassGroupId ClassGroupId { get; }

    /// <summary>
    /// Gets the academic term (e.g. "Spring", "September Retake").
    /// </summary>
    public string Term { get; }

    /// <summary>
    /// Gets the offering year.
    /// </summary>
    public int Year { get; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CourseOffering"/> class.
    /// </summary>
    public CourseOffering(CourseOfferingId id, InstitutionId institutionId, CourseId courseId,
        ClassGroupId classGroupId, string term, int year, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionId.Value, nameof(institutionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(courseId.Value, nameof(courseId));
        ArgumentException.ThrowIfNullOrWhiteSpace(classGroupId.Value, nameof(classGroupId));
        ArgumentException.ThrowIfNullOrWhiteSpace(term);
        if (year <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(year), "Year must be a positive integer.");
        }

        if (createdAt == default)
        {
            throw new ArgumentException("CreatedAt must be a valid timestamp.", nameof(createdAt));
        }

        Id = id;
        InstitutionId = institutionId;
        CourseId = courseId;
        ClassGroupId = classGroupId;
        Term = term;
        Year = year;
        CreatedAt = createdAt;
    }
}