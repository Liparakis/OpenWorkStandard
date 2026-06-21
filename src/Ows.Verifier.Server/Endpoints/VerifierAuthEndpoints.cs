namespace Ows.Verifier.Server;

/// <summary>
/// Provides route endpoint mapping extension methods for OWS verifier API key management.
/// </summary>
internal static class VerifierAuthEndpoints {
    /// <summary>
    /// Maps the auth management endpoints (API keys creation, listing, and revocation).
    /// </summary>
    /// <param name="app">The route builder application instance.</param>
    /// <returns>The route builder with endpoints mapped.</returns>
    public static void MapVerifierAuthEndpoints(this IEndpointRouteBuilder app) {
        var auditLogger = app.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit");

        app.MapPost("/auth/api-keys", async (HttpContext context, VerifierApiKeyCreateRequest request,
            IVerifierApiKeyStore apiKeyStore, IVerifierAuditStore auditStore, CancellationToken cancellationToken) => {
                var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
                if (!VerifierRolePolicy.IsOperatorRole(callerAccess?.Role ?? string.Empty)) {
                    if (!VerifierRolePolicy.IsInstitutionAdminRole(callerAccess?.Role ?? string.Empty)) {
                        return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }

                    var targetRole = VerifierRolePolicy.NormalizeRoleName(request.Role);
                    var isAllowedTargetRole =
                        string.Equals(targetRole, VerifierRolePolicy.InstructorReviewer, StringComparison.Ordinal) ||
                        string.Equals(targetRole, VerifierRolePolicy.StudentClient, StringComparison.Ordinal);
                    if (!isAllowedTargetRole) {
                        return Results.Json(
                            new { error = "InstitutionAdmin may only create InstructorReviewer or StudentClient keys." },
                            statusCode: StatusCodes.Status403Forbidden);
                    }

                    if (!string.Equals(request.InstitutionId, callerAccess!.InstitutionId,
                            StringComparison.OrdinalIgnoreCase)) {
                        return Results.Json(
                            new { error = "InstitutionAdmin may only create keys for their own institution." },
                            statusCode: StatusCodes.Status403Forbidden);
                    }
                }

                try {
                    var result = await apiKeyStore.CreateAsync(request, cancellationToken);
                    await VerifierAuditHelpers.WriteAuditEventAsync(
                        auditStore,
                        auditLogger,
                        context,
                        eventType: "api_key.created",
                        result: "Created",
                        access: callerAccess,
                        institutionId: result.Metadata.InstitutionId,
                        metadata: VerifierAuditHelpers.CreateMetadata(
                            ("createdKeyId", result.Metadata.KeyId),
                            ("createdKeyPrefix", result.Metadata.KeyPrefix),
                            ("createdRole", result.Metadata.Role),
                            ("expiresAtUtc", result.Metadata.ExpiresAtUtc?.ToString("o"))),
                        cancellationToken: cancellationToken);
                    return Results.Ok(new {
                        apiKey = result.ApiKey,
                        metadata = new {
                            keyId = result.Metadata.KeyId,
                            keyPrefix = result.Metadata.KeyPrefix,
                            role = result.Metadata.Role,
                            institutionId = result.Metadata.InstitutionId,
                            studentUserId = result.Metadata.StudentUserId,
                            createdAtUtc = result.Metadata.CreatedAtUtc,
                            expiresAtUtc = result.Metadata.ExpiresAtUtc,
                            lastUsedAtUtc = result.Metadata.LastUsedAtUtc,
                            revokedAtUtc = result.Metadata.RevokedAtUtc
                        }
                    });
                } catch (InvalidOperationException exception) {
                    return Results.BadRequest(exception.Message);
                }
            })
            .RequireRateLimiting(VerifierRateLimitingRegistration.AuthPolicy);

        app.MapGet("/auth/api-keys", async (IVerifierApiKeyStore apiKeyStore, CancellationToken cancellationToken) => {
            var keys = await apiKeyStore.ListAsync(cancellationToken);
            return Results.Ok(keys.Select(m => new {
                keyId = m.KeyId,
                keyPrefix = m.KeyPrefix,
                role = m.Role,
                institutionId = m.InstitutionId,
                studentUserId = m.StudentUserId,
                createdAtUtc = m.CreatedAtUtc,
                expiresAtUtc = m.ExpiresAtUtc,
                lastUsedAtUtc = m.LastUsedAtUtc,
                revokedAtUtc = m.RevokedAtUtc
            }));
        })
        .RequireRateLimiting(VerifierRateLimitingRegistration.DiagnosticsPolicy);

        app.MapPost("/auth/api-keys/{id}/revoke", async (HttpContext context, string id,
            IVerifierApiKeyStore apiKeyStore,
            IVerifierAuditStore auditStore, CancellationToken cancellationToken) => {
                var revoked = await apiKeyStore.RevokeAsync(id, cancellationToken);
                if (!revoked) {
                    return Results.NotFound("Unknown verifier API key.");
                }

                await VerifierAuditHelpers.WriteAuditEventAsync(
                    auditStore,
                    auditLogger,
                    context,
                    eventType: "api_key.revoked",
                    result: "Revoked",
                    access: VerifierAuthorizationHelpers.TryGetAccessContext(context),
                    metadata: VerifierAuditHelpers.CreateMetadata(("revokedKeyId", id)),
                    cancellationToken: cancellationToken);
                return Results.Ok(new { keyId = id, revoked = true });
            })
            .RequireRateLimiting(VerifierRateLimitingRegistration.AuthPolicy);
    }
}
