using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Ows.Cli.Tests")]

namespace Ows.Verifier.Server;

/// <summary>
/// Configures the verifier's API access guard.
/// </summary>
public sealed record VerifierSecurityOptions
{
    /// <summary>
    /// Gets the optional legacy operator API key required for verifier requests.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets the request header name carrying the shared API key.
    /// </summary>
    public string HeaderName { get; init; } = "X-OWS-Verifier-Key";

    /// <summary>
    /// Gets the configured verifier API keys and their scopes.
    /// </summary>
    public IReadOnlyList<VerifierApiKeyOptions> ApiKeys { get; init; } = [];
}

/// <summary>
/// Describes one configured verifier API key and its access scope.
/// </summary>
public sealed record VerifierApiKeyOptions
{
    /// <summary>
    /// Gets the API key secret.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets the role granted by the key. Supported values are <c>Operator</c>, <c>InstitutionAdmin</c>,
    /// <c>InstructorReviewer</c>, and <c>StudentClient</c>.
    /// </summary>
    public string Role { get; init; } = "operator";

    /// <summary>
    /// Gets the optional institution scope. InstitutionAdmin, InstructorReviewer, and StudentClient keys must set this.
    /// </summary>
    public string? InstitutionId { get; init; }
}

/// <summary>
/// Normalizes and validates the currently supported verifier roles.
/// </summary>
internal static class VerifierRolePolicy
{
    /// <summary>
    /// Gets the full-access operator role name.
    /// </summary>
    public const string Operator = "Operator";

    /// <summary>
    /// Gets the institution-scoped admin role name.
    /// </summary>
    public const string InstitutionAdmin = "InstitutionAdmin";

    /// <summary>
    /// Gets the institution-scoped read-only reviewer role name.
    /// </summary>
    public const string InstructorReviewer = "InstructorReviewer";

    /// <summary>
    /// Gets the student client role name.
    /// </summary>
    public const string StudentClient = "StudentClient";

    /// <summary>
    /// Normalizes a configured or requested role name.
    /// </summary>
    public static string NormalizeRoleName(string role)
    {
        var normalized = role?.Trim().Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "operator" => Operator,
            "institutionadmin" => InstitutionAdmin,
            "admin" => InstitutionAdmin,
            "reviewer" => InstructorReviewer,
            "instructorreviewer" => InstructorReviewer,
            "studentclient" => StudentClient,
            "student" => StudentClient,
            "client" => StudentClient,
            _ => role?.Trim() ?? string.Empty
        };
    }

    /// <summary>
    /// Returns whether the role is currently supported.
    /// </summary>
    public static bool IsSupportedRole(string role)
    {
        var normalized = NormalizeRoleName(role);
        return string.Equals(normalized, Operator, StringComparison.Ordinal) ||
               string.Equals(normalized, InstitutionAdmin, StringComparison.Ordinal) ||
               string.Equals(normalized, InstructorReviewer, StringComparison.Ordinal) ||
               string.Equals(normalized, StudentClient, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns whether the role is the full-access operator role.
    /// </summary>
    public static bool IsOperatorRole(string role) =>
        string.Equals(NormalizeRoleName(role), Operator, StringComparison.Ordinal);

    /// <summary>
    /// Returns whether the role is the institution-scoped admin role.
    /// </summary>
    public static bool IsInstitutionAdminRole(string role) =>
        string.Equals(NormalizeRoleName(role), InstitutionAdmin, StringComparison.Ordinal);

    /// <summary>
    /// Returns whether the role is the institution-scoped reviewer role.
    /// </summary>
    public static bool IsInstructorReviewerRole(string role) =>
        string.Equals(NormalizeRoleName(role), InstructorReviewer, StringComparison.Ordinal);

    /// <summary>
    /// Returns whether the role is the student client role.
    /// </summary>
    public static bool IsStudentClientRole(string role) =>
        string.Equals(NormalizeRoleName(role), StudentClient, StringComparison.Ordinal);

    /// <summary>
    /// Returns whether the role is institution-scoped (requires InstitutionId).
    /// </summary>
    public static bool IsInstitutionScopedRole(string role) =>
        IsInstitutionAdminRole(role) || IsInstructorReviewerRole(role) || IsStudentClientRole(role);

    /// <summary>
    /// Normalizes an institution identifier.
    /// </summary>
    public static string? NormalizeInstitutionId(string? institutionId) =>
        string.IsNullOrWhiteSpace(institutionId) ? null : institutionId.Trim();
}
