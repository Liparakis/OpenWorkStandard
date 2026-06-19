using System;

namespace Ows.Core.Education;

/// <summary>
/// Represents a participant (student, instructor, admin) in OWS.
/// </summary>
public sealed record User
{
    /// <summary>
    /// Gets the unique user identifier.
    /// </summary>
    public UserId Id { get; init; }

    /// <summary>
    /// Gets the institution identifier this user belongs to.
    /// </summary>
    public InstitutionId InstitutionId { get; init; }

    /// <summary>
    /// Gets the display name of the user.
    /// </summary>
    public string DisplayName { get; init; }

    /// <summary>
    /// Gets the optional external system identifier (e.g. Student ID).
    /// </summary>
    public string? ExternalId { get; init; }

    /// <summary>
    /// Gets the optional email address.
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="User"/> class.
    /// </summary>
    public User(UserId id, InstitutionId institutionId, string displayName, string? externalId, string? email, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionId.Value, nameof(institutionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName, nameof(displayName));
        if (createdAt == default)
        {
            throw new ArgumentException("CreatedAt must be a valid timestamp.", nameof(createdAt));
        }

        Id = id;
        InstitutionId = institutionId;
        DisplayName = displayName;
        ExternalId = externalId;
        Email = email;
        CreatedAt = createdAt;
    }
}
