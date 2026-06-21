using System.Security.Claims;

namespace Ows.Verifier.Server;

/// <summary>
/// Provides helper methods to map claims from an OIDC principal to a verifier access context.
/// </summary>
internal static class OidcPrincipalMapper {
    /// <summary>
    /// Attempts to map a <see cref="ClaimsPrincipal"/> to a <see cref="VerifierAccessContext"/> using the configured OIDC options.
    /// </summary>
    /// <param name="principal">The incoming claims principal representing the authenticated caller.</param>
    /// <param name="options">The OIDC configurations detailing claim names.</param>
    /// <returns>An <see cref="OidcPrincipalMappingResult"/> containing the access context or failure details.</returns>
    public static OidcPrincipalMappingResult TryMap(ClaimsPrincipal principal, VerifierOidcOptions options) {
        var roleValue = GetClaimValue(principal, options.RoleClaim);
        if (string.IsNullOrWhiteSpace(roleValue)) {
            return OidcPrincipalMappingResult.Fail(
                "MissingRoleClaim",
                $"Required role claim '{options.RoleClaim}' was not present.",
                403);
        }

        var normalizedRole = VerifierRolePolicy.NormalizeRoleName(roleValue);
        if (!VerifierRolePolicy.IsSupportedRole(normalizedRole)) {
            return OidcPrincipalMappingResult.Fail(
                "InvalidRoleClaim",
                $"Role claim '{options.RoleClaim}' value is not supported.",
                403);
        }

        var institutionId =
            VerifierRolePolicy.NormalizeInstitutionId(GetClaimValue(principal, options.InstitutionClaim));
        if (VerifierRolePolicy.IsInstitutionScopedRole(normalizedRole) && string.IsNullOrWhiteSpace(institutionId)) {
            return OidcPrincipalMappingResult.Fail(
                "MissingInstitutionClaim",
                $"Required institution claim '{options.InstitutionClaim}' was not present.",
                403);
        }

        var userId = GetClaimValue(principal, options.UserIdClaim);
        if (VerifierRolePolicy.IsStudentClientRole(normalizedRole) && string.IsNullOrWhiteSpace(userId)) {
            return OidcPrincipalMappingResult.Fail(
                "MissingUserIdClaim",
                $"Required user claim '{options.UserIdClaim}' was not present for StudentClient access.",
                403);
        }

        return OidcPrincipalMappingResult.Success(new VerifierAccessContext(
            normalizedRole,
            institutionId,
            key: string.Empty,
            studentUserId: VerifierRolePolicy.IsStudentClientRole(normalizedRole) ? userId : null,
            authenticationType: "Bearer",
            actorUserId: userId,
            actorEmail: GetClaimValue(principal, options.EmailClaim),
            actorDisplayName: GetClaimValue(principal, options.DisplayNameClaim)));
    }

    /// <summary>
    /// Extracts the value of a specific claim from the claims principal.
    /// </summary>
    /// <param name="principal">The claims principal.</param>
    /// <param name="claimType">The claim type name to look for.</param>
    /// <returns>The string value of the claim, or null if not found or the claim type is empty.</returns>
    private static string? GetClaimValue(ClaimsPrincipal principal, string claimType) {
        if (string.IsNullOrWhiteSpace(claimType)) {
            return null;
        }

        return principal.Claims.FirstOrDefault(claim =>
            string.Equals(claim.Type, claimType, StringComparison.OrdinalIgnoreCase))?.Value;
    }
}

/// <summary>
/// Represents the result of an OIDC principal mapping attempt.
/// </summary>
internal sealed record OidcPrincipalMappingResult {
    /// <summary>
    /// Gets a value indicating whether the mapping was successful.
    /// </summary>
    public bool Succeeded { get; private init; }

    /// <summary>
    /// Gets the mapped access context if successful; otherwise, null.
    /// </summary>
    public VerifierAccessContext? AccessContext { get; private init; }

    /// <summary>
    /// Gets the failure code or type if mapping failed; otherwise, null.
    /// </summary>
    public string? FailureResult { get; private init; }

    /// <summary>
    /// Gets the detailed failure message if mapping failed; otherwise, null.
    /// </summary>
    public string? FailureMessage { get; private init; }

    /// <summary>
    /// Gets the HTTP status code representing the mapping outcome.
    /// </summary>
    public int StatusCode { get; private init; }

    /// <summary>
    /// Creates a successful mapping result.
    /// </summary>
    /// <param name="accessContext">The successfully mapped access context.</param>
    /// <returns>A successful <see cref="OidcPrincipalMappingResult"/>.</returns>
    public static OidcPrincipalMappingResult Success(VerifierAccessContext accessContext) =>
        new() {
            Succeeded = true,
            AccessContext = accessContext,
            StatusCode = 200
        };

    /// <summary>
    /// Creates a failed mapping result.
    /// </summary>
    /// <param name="result">The failure code or category.</param>
    /// <param name="message">The failure message explanation.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <returns>A failed <see cref="OidcPrincipalMappingResult"/>.</returns>
    public static OidcPrincipalMappingResult Fail(string result, string message, int statusCode) =>
        new() {
            Succeeded = false,
            FailureResult = result,
            FailureMessage = message,
            StatusCode = statusCode
        };
}
