namespace Ows.Core.Education;

/// <summary>
/// Represents a university, school, or organization within OWS.
/// </summary>
public sealed record Institution {
    /// <summary>
    /// Gets the unique institution identifier.
    /// </summary>
    public InstitutionId Id { get; init; }

    /// <summary>
    /// Gets the name of the institution.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// Gets the URL slug for the institution.
    /// </summary>
    public string Slug { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Institution"/> class.
    /// </summary>
    public Institution(InstitutionId id, string name, string slug, DateTimeOffset createdAt) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        if (createdAt == default) {
            throw new ArgumentException("CreatedAt must be a valid timestamp.", nameof(createdAt));
        }

        Id = id;
        Name = name;
        Slug = slug;
        CreatedAt = createdAt;
    }
}
