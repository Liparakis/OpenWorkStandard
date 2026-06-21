using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ows.Core.Education;
using Ows.Core.Notarization;

namespace Ows.Verifier.Server;

/// <summary>
/// Provides helper methods for authentication and authorization logic on the OWS Verifier Server.
/// </summary>
internal static class VerifierAuthorizationHelpers
{
    /// <summary>
    /// Returns the attached <see cref="VerifierAccessContext"/> from the HTTP context items.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The attached access context, or null if authentication did not run or fail.</returns>
    public static VerifierAccessContext? TryGetAccessContext(HttpContext context) =>
        context.Items.TryGetValue("VerifierAccessContext", out var value) ? value as VerifierAccessContext : null;

    /// <summary>
    /// Extracts the bearer token from the request's Authorization header.
    /// </summary>
    /// <param name="request">The incoming HTTP request.</param>
    /// <returns>The bearer token string, or null if header is not present or invalid.</returns>
    public static string? TryGetSuppliedBearerToken(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Authorization", out var values))
        {
            return null;
        }

        var headerValue = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerValue) ||
            !headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = headerValue["Bearer ".Length..].Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    /// <summary>
    /// Builds access contexts for all API keys configured via builder settings (bootstrap keys).
    /// </summary>
    /// <param name="options">The verifier security options.</param>
    /// <returns>A list of bootstrap <see cref="VerifierAccessContext"/> instances.</returns>
    public static List<VerifierAccessContext> BuildConfiguredApiKeys(VerifierSecurityOptions options)
    {
        var configuredKeys = new List<VerifierAccessContext>();
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            configuredKeys.Add(new VerifierAccessContext(
                VerifierRolePolicy.Operator,
                null,
                options.ApiKey,
                null,
                VerifierServerHelpers.CreateSafeKeyPrefix(options.ApiKey)));
        }

        foreach (var apiKey in options.ApiKeys)
        {
            if (string.IsNullOrWhiteSpace(apiKey.Key))
            {
                continue;
            }

            configuredKeys.Add(new VerifierAccessContext(
                VerifierRolePolicy.NormalizeRoleName(apiKey.Role),
                apiKey.InstitutionId,
                apiKey.Key,
                null,
                VerifierServerHelpers.CreateSafeKeyPrefix(apiKey.Key)));
        }

        return configuredKeys;
    }

    /// <summary>
    /// Tries to get the supplied API key from the request headers using the configured header name.
    /// </summary>
    /// <param name="request">The HTTP request.</param>
    /// <param name="headerName">The name of the header carrying the API key.</param>
    /// <returns>The supplied key string, or null if header is missing or empty.</returns>
    public static string? TryGetSuppliedApiKey(HttpRequest request, string headerName)
    {
        if (string.IsNullOrWhiteSpace(headerName) ||
            !request.Headers.TryGetValue(headerName, out var suppliedValues))
        {
            return null;
        }

        var suppliedKey = suppliedValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(suppliedKey))
        {
            return null;
        }

        return suppliedKey;
    }

    /// <summary>
    /// Checks supplied API key material against configured bootstrap keys using a fixed-time comparison.
    /// </summary>
    /// <param name="suppliedKey">The incoming API key material.</param>
    /// <param name="configuredApiKeys">The list of configured API keys.</param>
    /// <returns>The matching access context if authenticated; otherwise null.</returns>
    public static VerifierAccessContext? TryAuthenticateConfiguredApiKey(
        string suppliedKey,
        IReadOnlyList<VerifierAccessContext> configuredApiKeys)
    {
        var suppliedBytes = Encoding.UTF8.GetBytes(suppliedKey);
        foreach (var configuredApiKey in configuredApiKeys)
        {
            var expectedBytes = Encoding.UTF8.GetBytes(configuredApiKey.Key);
            if (suppliedBytes.Length == expectedBytes.Length &&
                CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes))
            {
                return configuredApiKey;
            }
        }

        return null;
    }

    /// <summary>
    /// Tries to authenticate the supplied key against persisted API keys stored in the database/JSON file.
    /// </summary>
    /// <param name="apiKeyStore">The verifier API key store.</param>
    /// <param name="suppliedKey">The incoming API key material.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The authenticated access context, or null if not found or invalid.</returns>
    public static async Task<VerifierAccessContext?> TryAuthenticatePersistedApiKeyAsync(
        IVerifierApiKeyStore apiKeyStore,
        string suppliedKey,
        CancellationToken cancellationToken)
    {
        try
        {
            return await apiKeyStore.AuthenticateAsync(suppliedKey, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Enforces the role-based access control policies for OWS endpoints.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="access">The validated access context of the caller.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the caller is authorized for the endpoint; otherwise false.</returns>
    public static async Task<bool> IsAuthorizedAsync(
        HttpContext context,
        VerifierAccessContext access,
        CancellationToken cancellationToken)
    {
        if (VerifierRolePolicy.IsOperatorRole(access.Role))
        {
            return true;
        }

        var isAdmin = VerifierRolePolicy.IsInstitutionAdminRole(access.Role);
        var isReviewer = VerifierRolePolicy.IsInstructorReviewerRole(access.Role);
        var isStudent = VerifierRolePolicy.IsStudentClientRole(access.Role);

        var segments = context.Request.Path.Value?
                           .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       ?? [];
        if (segments.Length == 0)
        {
            return false;
        }

        if (segments is ["auth", "api-keys"])
        {
            if (HttpMethods.IsGet(context.Request.Method)) return false;
            return isAdmin;
        }

        if (segments is ["auth", "api-keys", _, "revoke"])
        {
            return false;
        }

        if (segments is ["audit", "events"])
        {
            return isAdmin && HttpMethods.IsGet(context.Request.Method);
        }

        if (segments is ["diagnostics", "summary"])
        {
            return false;
        }

        var isGet = HttpMethods.IsGet(context.Request.Method);
        var isWrite = !isGet;

        if (segments is ["sessions"])
        {
            if (isWrite)
            {
                return isAdmin || isStudent;
            }

            return false;
        }

        if (segments is ["sessions", _, "heartbeat"] || segments is ["sessions", _, "checkpoints"])
        {
            var sessionId = segments[1];
            if (isWrite)
            {
                return isStudent && await MatchesStudentSessionScopeAsync(
                    sessionId,
                    access,
                    context.RequestServices.GetRequiredService<IVerifierStorage>(),
                    cancellationToken);
            }

            return false;
        }

        if (segments is ["sessions", _, "packages"] || segments is ["sessions", _, "receipts"] ||
            segments is ["sessions", _, "head"])
        {
            var sessionId = segments[1];
            if (isWrite) return false;
            if (isStudent)
            {
                return await MatchesStudentSessionScopeAsync(
                    sessionId,
                    access,
                    context.RequestServices.GetRequiredService<IVerifierStorage>(),
                    cancellationToken);
            }

            return (isAdmin || isReviewer) && await MatchesInstitutionScopeAsync(
                null,
                sessionId,
                access,
                context.RequestServices.GetRequiredService<IVerifierStorage>(),
                cancellationToken);
        }

        if (segments is ["packages"] or ["packages", "upload"])
        {
            if (isWrite)
            {
                return isStudent;
            }

            return false;
        }

        if (segments is ["packages", _, "verify"])
        {
            var packageId = segments[1];
            if (isWrite)
            {
                return isStudent && await MatchesStudentPackageScopeAsync(
                    packageId,
                    access,
                    context.RequestServices.GetRequiredService<IPackageSubmissionStore>(),
                    cancellationToken);
            }

            return false;
        }

        if (segments is ["packages", _] || segments is ["packages", _, "verification"] ||
            segments is ["packages", _, "report"])
        {
            var packageId = segments[1];
            if (isWrite)
            {
                if (segments.Length == 2)
                {
                    return isStudent && await MatchesStudentPackageScopeAsync(
                        packageId,
                        access,
                        context.RequestServices.GetRequiredService<IPackageSubmissionStore>(),
                        cancellationToken);
                }

                return false;
            }

            if (isStudent)
            {
                return await MatchesStudentPackageScopeAsync(
                    packageId,
                    access,
                    context.RequestServices.GetRequiredService<IPackageSubmissionStore>(),
                    cancellationToken);
            }

            var packageStore = context.RequestServices.GetRequiredService<IPackageSubmissionStore>();
            var submission = await packageStore.GetAsync(packageId, cancellationToken);
            return submission is not null && (isAdmin || isReviewer) && await MatchesInstitutionScopeAsync(
                submission.InstitutionId,
                submission.SessionId,
                access,
                context.RequestServices.GetRequiredService<IVerifierStorage>(),
                cancellationToken);
        }

        if (segments.Length >= 2 && string.Equals(segments[0], "education", StringComparison.OrdinalIgnoreCase))
        {
            var educationStore = context.RequestServices.GetRequiredService<IEducationStore>();

            if (isWrite)
            {
                if (!isAdmin) return false;

                if (segments.Length == 2)
                {
                    return true;
                }

                var institutionId =
                    await ResolveEducationInstitutionIdAsync(segments, educationStore, cancellationToken);
                return !string.IsNullOrWhiteSpace(institutionId) &&
                       string.Equals(institutionId, access.InstitutionId, StringComparison.OrdinalIgnoreCase);
            }

            if (!isAdmin && !isReviewer) return false;
            var resolvedInstitutionId =
                await ResolveEducationInstitutionIdAsync(segments, educationStore, cancellationToken);
            return !string.IsNullOrWhiteSpace(resolvedInstitutionId) &&
                   string.Equals(resolvedInstitutionId, access.InstitutionId, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Checks if the session or institution ID matches the institution ID allowed by the caller's access context.
    /// </summary>
    /// <param name="institutionId">The institution ID to verify.</param>
    /// <param name="sessionId">The session ID to query from storage if institution ID is omitted.</param>
    /// <param name="access">The validated access context.</param>
    /// <param name="storage">The verifier storage.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the scope matches; otherwise false.</returns>
    private static async Task<bool> MatchesInstitutionScopeAsync(
        string? institutionId,
        string? sessionId,
        VerifierAccessContext access,
        IVerifierStorage storage,
        CancellationToken cancellationToken)
    {
        var resolvedInstitutionId = VerifierRolePolicy.NormalizeInstitutionId(institutionId);
        if (string.IsNullOrWhiteSpace(resolvedInstitutionId) && !string.IsNullOrWhiteSpace(sessionId))
        {
            try
            {
                var session = await storage.GetSessionAsync(new AssessmentSessionId(sessionId), cancellationToken);
                resolvedInstitutionId =
                    VerifierRolePolicy.NormalizeInstitutionId(TryGetInstitutionIdFromMetadata(session.MetadataJson));
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        return !string.IsNullOrWhiteSpace(resolvedInstitutionId) &&
               string.Equals(resolvedInstitutionId, access.InstitutionId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that the session belongs to both the caller's institution and is owned by the student caller (if student-scoped).
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="access">The validated access context.</param>
    /// <param name="storage">The verifier storage.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the student caller is authorized to access the session; otherwise false.</returns>
    private static async Task<bool> MatchesStudentSessionScopeAsync(
        string sessionId,
        VerifierAccessContext access,
        IVerifierStorage storage,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await storage.GetSessionAsync(new AssessmentSessionId(sessionId), cancellationToken);

            var sessionInstId =
                VerifierRolePolicy.NormalizeInstitutionId(TryGetInstitutionIdFromMetadata(session.MetadataJson));
            if (string.IsNullOrWhiteSpace(sessionInstId) ||
                !string.Equals(sessionInstId, access.InstitutionId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(access.StudentUserId))
            {
                return true;
            }

            var sessionStudentId = TryGetMetadataValue(session.MetadataJson, "studentUserId");
            if (!string.Equals(sessionStudentId, access.StudentUserId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies that the package belongs to both the caller's institution and is owned by the student caller.
    /// </summary>
    /// <param name="packageId">The package submission ID.</param>
    /// <param name="access">The validated access context.</param>
    /// <param name="packageStore">The package submission store.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True if the student caller matches the package owner; otherwise false.</returns>
    private static async Task<bool> MatchesStudentPackageScopeAsync(
        string packageId,
        VerifierAccessContext access,
        IPackageSubmissionStore packageStore,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(access.StudentUserId))
        {
            return false;
        }

        try
        {
            var submission = await packageStore.GetAsync(packageId, cancellationToken);
            if (submission is null)
            {
                return false;
            }

            var packageInstId = VerifierRolePolicy.NormalizeInstitutionId(submission.InstitutionId);
            if (string.IsNullOrWhiteSpace(packageInstId) ||
                !string.Equals(packageInstId, access.InstitutionId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(submission.StudentUserId, access.StudentUserId, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts the institutionId from a session's metadata JSON.
    /// </summary>
    /// <param name="metadataJson">The metadata JSON string.</param>
    /// <returns>The institution ID string, or null if not found or invalid.</returns>
    public static string? TryGetInstitutionIdFromMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return document.RootElement.TryGetProperty("institutionId", out var institutionIdElement)
                ? institutionIdElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts a metadata property value from the JSON string.
    /// </summary>
    /// <param name="metadataJson">The JSON metadata string.</param>
    /// <param name="propertyName">The property key name.</param>
    /// <returns>The property value string, or null if not found or invalid.</returns>
    public static string? TryGetMetadataValue(string? metadataJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadataJson) || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return document.RootElement.TryGetProperty(propertyName, out var element) ? element.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the institution ID scope for a given education API route segment.
    /// </summary>
    /// <param name="segments">The split path segments array.</param>
    /// <param name="educationStore">The education store.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The institution ID, or null if not resolvable.</returns>
    private static async Task<string?> ResolveEducationInstitutionIdAsync(
        string[] segments,
        IEducationStore educationStore,
        CancellationToken cancellationToken)
    {
        return segments switch
        {
            ["education", "institutions", var institutionIdSegment] => institutionIdSegment,
            ["education", "courses", var courseId] => (await educationStore.GetCourseAsync(new CourseId(courseId),
                cancellationToken))?.InstitutionId.Value,
            ["education", "class-groups", var classGroupId] => (await educationStore.GetClassGroupAsync(
                new ClassGroupId(classGroupId), cancellationToken))?.InstitutionId.Value,
            ["education", "course-offerings", var offeringId] => (await educationStore.GetCourseOfferingAsync(
                new CourseOfferingId(offeringId), cancellationToken))?.InstitutionId.Value,
            ["education", "assessments", var assessmentId] => (await educationStore.GetAssessmentAsync(
                new AssessmentId(assessmentId), cancellationToken))?.InstitutionId.Value,
            ["education", "users", var userId] => (await educationStore.GetUserAsync(new UserId(userId),
                cancellationToken))?.InstitutionId.Value,
            ["education", "enrollments", "user", var enrollmentUserId] => (await educationStore.GetUserAsync(
                new UserId(enrollmentUserId), cancellationToken))?.InstitutionId.Value,
            ["education", "enrollments", "offering", var enrollmentOfferingId] =>
                (await educationStore.GetCourseOfferingAsync(new CourseOfferingId(enrollmentOfferingId),
                    cancellationToken))?.InstitutionId.Value,
            _ => null
        };
    }
}