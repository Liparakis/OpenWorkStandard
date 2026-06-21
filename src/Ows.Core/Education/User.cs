using System;

namespace Ows.Core.Education;

/// <summary>
/// Represents a participant (student, instructor, admin) in OWS.
/// </summary>
public sealed record User {
    /// <summary>
    /// Gets the unique user identifier.
    /// </summary>
    public UserId Id { get; }

    /// <summary>
    /// Gets the institution identifier this user belongs to.
    /// </summary>
    public InstitutionId InstitutionId { get; }

    /// <summary>
    /// Gets the display name of the user.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the optional external system identifier (e.g. Student ID).
    /// </summary>
    public string? ExternalId { get; }

    /// <summary>
    /// Gets the optional email address.
    /// </summary>
    public string? Email { get; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="User"/> class.
    /// </summary>
    public User(UserId id, InstitutionId institutionId, string displayName, string? externalId, string? email,
        DateTimeOffset createdAt) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionId.Value, nameof(institutionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (createdAt == default) {
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
