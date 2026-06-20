using System.Security.Claims;

namespace Ows.Verifier.Server;

internal static class OidcPrincipalMapper
{
    public static OidcPrincipalMappingResult TryMap(ClaimsPrincipal principal, VerifierOidcOptions options)
    {
        var roleValue = GetClaimValue(principal, options.RoleClaim);
        if (string.IsNullOrWhiteSpace(roleValue))
        {
            return OidcPrincipalMappingResult.Fail(
                "MissingRoleClaim",
                $"Required role claim '{options.RoleClaim}' was not present.",
                403);
        }

        var normalizedRole = VerifierRolePolicy.NormalizeRoleName(roleValue);
        if (!VerifierRolePolicy.IsSupportedRole(normalizedRole))
        {
            return OidcPrincipalMappingResult.Fail(
                "InvalidRoleClaim",
                $"Role claim '{options.RoleClaim}' value is not supported.",
                403);
        }

        var institutionId = VerifierRolePolicy.NormalizeInstitutionId(GetClaimValue(principal, options.InstitutionClaim));
        if (VerifierRolePolicy.IsInstitutionScopedRole(normalizedRole) && string.IsNullOrWhiteSpace(institutionId))
        {
            return OidcPrincipalMappingResult.Fail(
                "MissingInstitutionClaim",
                $"Required institution claim '{options.InstitutionClaim}' was not present.",
                403);
        }

        var userId = GetClaimValue(principal, options.UserIdClaim);
        if (VerifierRolePolicy.IsStudentClientRole(normalizedRole) && string.IsNullOrWhiteSpace(userId))
        {
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

    private static string? GetClaimValue(ClaimsPrincipal principal, string claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType))
        {
            return null;
        }

        return principal.Claims.FirstOrDefault(claim =>
            string.Equals(claim.Type, claimType, StringComparison.OrdinalIgnoreCase))?.Value;
    }
}

internal sealed record OidcPrincipalMappingResult
{
    public bool Succeeded { get; private init; }

    public VerifierAccessContext? AccessContext { get; private init; }

    public string? FailureResult { get; private init; }

    public string? FailureMessage { get; private init; }

    public int StatusCode { get; private init; }

    public static OidcPrincipalMappingResult Success(VerifierAccessContext accessContext) =>
        new()
        {
            Succeeded = true,
            AccessContext = accessContext,
            StatusCode = 200
        };

    public static OidcPrincipalMappingResult Fail(string result, string message, int statusCode) =>
        new()
        {
            Succeeded = false,
            FailureResult = result,
            FailureMessage = message,
            StatusCode = statusCode
        };
}
