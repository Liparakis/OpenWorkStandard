using System;

namespace Ows.Core.Education;

/// <summary>
/// Represents a class, cohort, or group inside an institution.
/// </summary>
public sealed record ClassGroup
{
    /// <summary>
    /// Gets the class group identifier.
    /// </summary>
    public ClassGroupId Id { get; }

    /// <summary>
    /// Gets the institution identifier this class group belongs to.
    /// </summary>
    public InstitutionId InstitutionId { get; }

    /// <summary>
    /// Gets the name of the class cohort (e.g. "Section A", "Informatics 2026").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ClassGroup"/> class.
    /// </summary>
    public ClassGroup(ClassGroupId id, InstitutionId institutionId, string name, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionId.Value, nameof(institutionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (createdAt == default)
        {
            throw new ArgumentException("CreatedAt must be a valid timestamp.", nameof(createdAt));
        }

        Id = id;
        InstitutionId = institutionId;
        Name = name;
        CreatedAt = createdAt;
    }
}
