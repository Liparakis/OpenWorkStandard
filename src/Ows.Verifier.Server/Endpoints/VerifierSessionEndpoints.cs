using System.Text.Json;
using Ows.Core.Education;
using Ows.Core.Notarization;

namespace Ows.Verifier.Server;

/// <summary>
/// Provides route endpoint mapping extension methods for OWS assessment session management.
/// </summary>
internal static class VerifierSessionEndpoints {
    /// <summary>
    /// Maps the session lifecycle endpoints (session creation, heartbeats, checkpoints, receipts, head).
    /// </summary>
    /// <param name="app">The route builder application instance.</param>
    /// <returns>The route builder with endpoints mapped.</returns>
    public static void MapVerifierSessionEndpoints(this IEndpointRouteBuilder app) {
        var auditLogger = app.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit");

        app.MapPost("/sessions", async (HttpContext context, StartSessionRequest? body, IVerifierStorage storage,
            IEducationStore educationStore, IVerifierAuditStore auditStore, CancellationToken cancellationToken) => {
                var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
                if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role)) {
                    if (string.IsNullOrWhiteSpace(body?.InstitutionId) ||
                        !string.Equals(body.InstitutionId, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase)) {
                        return Results.Json(
                            new { error = $"{callerAccess.Role} may only create sessions for their own institution." },
                            statusCode: StatusCodes.Status403Forbidden);
                    }

                    if (VerifierRolePolicy.IsStudentClientRole(callerAccess.Role) &&
                        !string.IsNullOrWhiteSpace(callerAccess.StudentUserId)) {
                        if (string.IsNullOrWhiteSpace(body.StudentUserId) ||
                            !string.Equals(body.StudentUserId, callerAccess.StudentUserId,
                                StringComparison.OrdinalIgnoreCase)) {
                            return Results.Json(
                                new {
                                    error = "StudentClient bound key must match the student user ID in the session request."
                                },
                                statusCode: StatusCodes.Status403Forbidden);
                        }
                    }
                }

                string? clientId = body?.StudentUserId;
                string? assessmentId = body?.AssessmentId;
                string? metadataJson = null;

                // Validate education context when any field is supplied
                if (!string.IsNullOrWhiteSpace(body?.InstitutionId)
                    || !string.IsNullOrWhiteSpace(body?.AssessmentId)
                    || !string.IsNullOrWhiteSpace(body?.StudentUserId)) {
                    // Validate institution exists
                    if (string.IsNullOrWhiteSpace(body.InstitutionId)) {
                        return Results.BadRequest("InstitutionId is required when education context is supplied.");
                    }

                    var institution = await educationStore.GetInstitutionAsync(
                        new InstitutionId(body.InstitutionId), cancellationToken);
                    if (institution is null) {
                        return Results.BadRequest($"Institution '{body.InstitutionId}' not found.");
                    }

                    // Validate assessment belongs to the institution when supplied
                    if (!string.IsNullOrWhiteSpace(body.AssessmentId)) {
                        var assessment = await educationStore.GetAssessmentAsync(
                            new AssessmentId(body.AssessmentId), cancellationToken);
                        if (assessment is null) {
                            return Results.BadRequest($"Assessment '{body.AssessmentId}' not found.");
                        }

                        if (!string.Equals(assessment.InstitutionId.Value, body.InstitutionId,
                                StringComparison.OrdinalIgnoreCase)) {
                            return Results.BadRequest(
                                $"Assessment '{body.AssessmentId}' does not belong to institution '{body.InstitutionId}'.");
                        }
                    }

                    // Validate student exists when supplied
                    if (!string.IsNullOrWhiteSpace(body.StudentUserId)) {
                        var student = await educationStore.GetUserAsync(
                            new UserId(body.StudentUserId), cancellationToken);
                        if (student is null) {
                            return Results.BadRequest($"Student user '{body.StudentUserId}' not found.");
                        }
                    }

                    metadataJson = JsonSerializer.Serialize(new {
                        institutionId = body.InstitutionId,
                        assessmentId = body.AssessmentId,
                        studentUserId = body.StudentUserId,
                        courseOfferingId = body.CourseOfferingId
                    });
                }

                var session = await storage.CreateSessionAsync(clientId, assessmentId, metadataJson, cancellationToken);
                await VerifierAuditHelpers.WriteAuditEventAsync(
                    auditStore,
                    auditLogger,
                    context,
                    eventType: "session.created",
                    result: "Created",
                    access: VerifierAuthorizationHelpers.TryGetAccessContext(context),
                    institutionId: body?.InstitutionId,
                    sessionId: session.Id.Value,
                    assessmentId: assessmentId,
                    metadata: VerifierAuditHelpers.CreateMetadata(("clientId", clientId)),
                    cancellationToken: cancellationToken);
                return Results.Ok(new StartSessionResponse { SessionId = session.Id.Value });
            });

        app.MapPost("/sessions/{id}/heartbeat", async (string id, SessionHeartbeatRequest request,
            HttpContext context, IVerifierStorage storage, IVerifierAuditStore auditStore,
            CancellationToken cancellationToken) => {
                var sessionId = new AssessmentSessionId(id);
                try {
                    var leaseDuration = TimeSpan.FromSeconds(120);
                    var session = await storage.RecordHeartbeatAsync(
                        sessionId,
                        request.LastKnownEventHash,
                        leaseDuration,
                        cancellationToken);

                    var headResponse = new SessionHeadResponse {
                        SessionId = session.Id.Value,
                        LastSequenceNumber = session.CheckpointCount,
                        LastTimelineHeadHash = session.HeadEventHash,
                        LastReceiptHash = session.HeadReceiptHash
                    };

                    var response = new SessionHeartbeatResponse {
                        ServerTime = DateTimeOffset.UtcNow,
                        LeaseExpiresAt = session.LeaseExpiresAt ?? DateTimeOffset.UtcNow,
                        SessionTrustState = session.HasLeaseGap ? "Degraded" : "Active",
                        SessionHead = headResponse
                    };

                    var institutionId =
                        VerifierRolePolicy.NormalizeInstitutionId(
                            VerifierAuthorizationHelpers.TryGetInstitutionIdFromMetadata(session.MetadataJson));
                    await VerifierAuditHelpers.WriteAuditEventAsync(
                        auditStore,
                        auditLogger,
                        context,
                        eventType: "heartbeat.accepted",
                        result: session.HasLeaseGap ? "Degraded" : "Accepted",
                        access: VerifierAuthorizationHelpers.TryGetAccessContext(context),
                        institutionId: institutionId,
                        sessionId: session.Id.Value,
                        assessmentId: session.AssessmentId,
                        metadata: VerifierAuditHelpers.CreateMetadata(
                            ("lastKnownEventHash", request.LastKnownEventHash),
                            ("leaseExpiresAt", response.LeaseExpiresAt.ToString("o"))),
                        cancellationToken: cancellationToken);
                    if (session.HasLeaseGap) {
                        await VerifierAuditHelpers.WriteAuditEventAsync(
                            auditStore,
                            auditLogger,
                            context,
                            eventType: "lease.gap.detected",
                            result: "Degraded",
                            access: VerifierAuthorizationHelpers.TryGetAccessContext(context),
                            institutionId: institutionId,
                            sessionId: session.Id.Value,
                            assessmentId: session.AssessmentId,
                            metadata: VerifierAuditHelpers.CreateMetadata(
                                ("maxLeaseGapSeconds", session.MaxLeaseGapSeconds.ToString()),
                                ("firstLeaseGapAt", session.FirstLeaseGapAt?.ToString("o"))),
                            cancellationToken: cancellationToken);
                    }

                    return Results.Ok(response);
                } catch (InvalidOperationException) {
                    return Results.NotFound($"Unknown assessment session: {id}");
                }
            });

        app.MapPost("/sessions/{id}/checkpoints", async (string id, CheckpointRequest request, HttpRequest httpRequest,
            HttpContext context, IVerifierStorage storage, IVerifierAuditStore auditStore,
            CancellationToken cancellationToken) => {
                var idempotencyKey = httpRequest.Headers["Idempotency-Key"].FirstOrDefault();
                var validationError = request.GetValidationError(id, idempotencyKey);
                if (validationError is not null) {
                    return Results.BadRequest(validationError);
                }

                try {
                    var receipt = await storage.AppendCheckpointAsync(new Checkpoint {
                        SessionId = new AssessmentSessionId(request.SessionId),
                        SequenceNumber = request.SequenceNumber,
                        TimelineHeadHash = request.TimelineHeadHash,
                        IdempotencyKey = idempotencyKey
                    }, cancellationToken);
                    var session =
                        await storage.GetSessionAsync(new AssessmentSessionId(request.SessionId), cancellationToken);
                    var institutionId =
                        VerifierRolePolicy.NormalizeInstitutionId(
                            VerifierAuthorizationHelpers.TryGetInstitutionIdFromMetadata(session.MetadataJson));
                    await VerifierAuditHelpers.WriteAuditEventAsync(
                        auditStore,
                        auditLogger,
                        context,
                        eventType: "checkpoint.accepted",
                        result: session.HasLeaseGap ? "Degraded" : "Accepted",
                        access: VerifierAuthorizationHelpers.TryGetAccessContext(context),
                        institutionId: institutionId,
                        sessionId: request.SessionId,
                        assessmentId: session.AssessmentId,
                        metadata: VerifierAuditHelpers.CreateMetadata(
                            ("sequenceNumber", request.SequenceNumber.ToString()),
                            ("timelineHeadHash", request.TimelineHeadHash),
                            ("receiptHash", receipt.ReceiptHash),
                            ("idempotencyKey", idempotencyKey)),
                        cancellationToken: cancellationToken);
                    if (session.HasLeaseGap) {
                        await VerifierAuditHelpers.WriteAuditEventAsync(
                            auditStore,
                            auditLogger,
                            context,
                            eventType: "lease.gap.detected",
                            result: "Degraded",
                            access: VerifierAuthorizationHelpers.TryGetAccessContext(context),
                            institutionId: institutionId,
                            sessionId: request.SessionId,
                            assessmentId: session.AssessmentId,
                            metadata: VerifierAuditHelpers.CreateMetadata(
                                ("maxLeaseGapSeconds", session.HasLeaseGap ? session.MaxLeaseGapSeconds.ToString() : "0"),
                                ("firstLeaseGapAt", session.FirstLeaseGapAt?.ToString("o"))),
                            cancellationToken: cancellationToken);
                    }

                    return Results.Ok(receipt);
                } catch (InvalidOperationException exception) {
                    return Results.BadRequest(exception.Message);
                }
            });

        app.MapGet("/sessions/{id}/packages", async (string id, IPackageSubmissionStore packageStore,
            CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await packageStore.ListBySessionAsync(id, cancellationToken));
                } catch (NotSupportedException exception) {
                    return Results.BadRequest(exception.Message);
                }
            });

        app.MapGet("/sessions/{id}/receipts",
            async (string id, IVerifierStorage storage, CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await storage.GetReceiptsAsync(new AssessmentSessionId(id), cancellationToken));
                } catch (InvalidOperationException exception) {
                    return Results.NotFound(exception.Message);
                }
            });

        app.MapGet("/sessions/{id}/head",
            async (string id, IVerifierStorage storage, CancellationToken cancellationToken) => {
                try {
                    return Results.Ok(await storage.GetHeadAsync(new AssessmentSessionId(id), cancellationToken));
                } catch (InvalidOperationException exception) {
                    return Results.NotFound(exception.Message);
                }
            });
    }
}
