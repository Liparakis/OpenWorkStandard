namespace Ows.Core.Education;

/// <summary>
/// Represents a reusable course template/identity under an institution.
/// </summary>
public sealed record Course
{
    /// <summary>
    /// Gets the unique course identifier.
    /// </summary>
    public CourseId Id { get; }

    /// <summary>
    /// Gets the institution identifier this course belongs to.
    /// </summary>
    public InstitutionId InstitutionId { get; }

    /// <summary>
    /// Gets the course code (e.g. "CS101").
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the course title (e.g. "Introduction to Computer Science").
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Course"/> class.
    /// </summary>
    public Course(CourseId id, InstitutionId institutionId, string code, string title, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionId.Value, nameof(institutionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (createdAt == default)
        {
            throw new ArgumentException("CreatedAt must be a valid timestamp.", nameof(createdAt));
        }

        Id = id;
        InstitutionId = institutionId;
        Code = code;
        Title = title;
        CreatedAt = createdAt;
    }
}