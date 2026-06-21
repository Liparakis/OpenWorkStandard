namespace Ows.Verifier.Server;

/// <summary>
/// Provides route endpoint mapping extension methods for querying OWS verifier audit trail events.
/// </summary>
internal static class VerifierAuditEndpoints {
    /// <summary>
    /// Maps the verifier audit logs retrieval endpoint.
    /// </summary>
    /// <param name="app">The route builder application instance.</param>
    /// <returns>The route builder with endpoints mapped.</returns>
    public static void MapVerifierAuditEndpoints(this IEndpointRouteBuilder app) {
        app.MapGet("/audit/events", async (HttpContext context, string? institutionId, string? sessionId,
            string? packageId, string? eventType, DateTimeOffset? since, int? limit,
            IVerifierAuditStore auditStore, CancellationToken cancellationToken) => {
                var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);

                // InstitutionAdmin can only read their own institution's audit events.
                // Enforce the scope regardless of what the caller supplied.
                var effectiveInstitutionId = VerifierRolePolicy.IsInstitutionAdminRole(callerAccess?.Role ?? string.Empty)
                    ? callerAccess!.InstitutionId
                    : institutionId;

                var query = new VerifierAuditQuery {
                    InstitutionId = effectiveInstitutionId,
                    SessionId = sessionId,
                    PackageId = packageId,
                    EventType = eventType,
                    Since = since,
                    Limit = limit ?? 100
                };
                return Results.Ok(await auditStore.QueryAsync(query, cancellationToken));
            })
            .RequireRateLimiting(VerifierRateLimitingRegistration.DiagnosticsPolicy);
    }
}
