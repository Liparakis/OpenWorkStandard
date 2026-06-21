namespace Ows.Verifier.Server;

/// <summary>
/// Represents the security context and access details of an authenticated caller.
/// </summary>
internal sealed record VerifierAccessContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VerifierAccessContext"/> record.
    /// </summary>
    /// <param name="role">The normalized security role assigned to the caller.</param>
    /// <param name="institutionId">The optional institution identifier scoping this context.</param>
    /// <param name="key">The raw security key value.</param>
    /// <param name="keyId">The optional unique key identifier.</param>
    /// <param name="keyPrefix">The optional non-secret prefix of the API key.</param>
    /// <param name="studentUserId">The optional student user identifier if role is StudentClient.</param>
    /// <param name="authenticationType">The authentication type used, e.g., ApiKey or Bearer.</param>
    /// <param name="actorUserId">The optional unique identifier of the physical actor.</param>
    /// <param name="actorEmail">The optional email address of the physical actor.</param>
    /// <param name="actorDisplayName">The optional display name of the physical actor.</param>
    public VerifierAccessContext(
        string role,
        string? institutionId,
        string key,
        string? keyId = null,
        string? keyPrefix = null,
        string? studentUserId = null,
        string authenticationType = "ApiKey",
        string? actorUserId = null,
        string? actorEmail = null,
        string? actorDisplayName = null)
    {
        Role = string.IsNullOrWhiteSpace(role)
            ? VerifierRolePolicy.Operator
            : VerifierRolePolicy.NormalizeRoleName(role);
        InstitutionId = string.IsNullOrWhiteSpace(institutionId) ? null : institutionId.Trim();
        Key = key;
        KeyId = string.IsNullOrWhiteSpace(keyId) ? null : keyId.Trim();
        KeyPrefix = string.IsNullOrWhiteSpace(keyPrefix)
            ? (string.IsNullOrWhiteSpace(key) ? null : key[..Math.Min(12, key.Length)])
            : keyPrefix.Trim();
        StudentUserId = string.IsNullOrWhiteSpace(studentUserId) ? null : studentUserId.Trim();
        AuthenticationType = string.IsNullOrWhiteSpace(authenticationType) ? "ApiKey" : authenticationType.Trim();
        ActorUserId = string.IsNullOrWhiteSpace(actorUserId) ? null : actorUserId.Trim();
        ActorEmail = string.IsNullOrWhiteSpace(actorEmail) ? null : actorEmail.Trim();
        ActorDisplayName = string.IsNullOrWhiteSpace(actorDisplayName) ? null : actorDisplayName.Trim();
    }

    /// <summary>
    /// Gets the normalized security role assigned to the caller.
    /// </summary>
    public string Role { get; }

    /// <summary>
    /// Gets the optional institution identifier scoping this context.
    /// </summary>
    public string? InstitutionId { get; }

    /// <summary>
    /// Gets the raw security key value.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the optional unique key identifier.
    /// </summary>
    public string? KeyId { get; }

    /// <summary>
    /// Gets the optional non-secret prefix of the API key.
    /// </summary>
    public string? KeyPrefix { get; }

    /// <summary>
    /// Gets the optional student user identifier if role is StudentClient.
    /// </summary>
    public string? StudentUserId { get; }

    /// <summary>
    /// Gets the authentication type used, e.g., ApiKey or Bearer.
    /// </summary>
    public string AuthenticationType { get; }

    /// <summary>
    /// Gets the optional unique identifier of the physical actor.
    /// </summary>
    public string? ActorUserId { get; }

    /// <summary>
    /// Gets the optional email address of the physical actor.
    /// </summary>
    public string? ActorEmail { get; }

    /// <summary>
    /// Gets the optional display name of the physical actor.
    /// </summary>
    public string? ActorDisplayName { get; }
}