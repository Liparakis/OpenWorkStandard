namespace Ows.Verifier.Server;

sealed record VerifierAccessContext
{
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
        Role = string.IsNullOrWhiteSpace(role) ? VerifierRolePolicy.Operator : VerifierRolePolicy.NormalizeRoleName(role);
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

    public string Role { get; init; }

    public string? InstitutionId { get; init; }

    public string Key { get; init; }

    public string? KeyId { get; init; }

    public string? KeyPrefix { get; init; }

    public string? StudentUserId { get; init; }

    public string AuthenticationType { get; init; }

    public string? ActorUserId { get; init; }

    public string? ActorEmail { get; init; }

    public string? ActorDisplayName { get; init; }
}
