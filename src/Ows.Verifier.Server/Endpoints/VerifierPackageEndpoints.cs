using System.Text.Json;
using Ows.Core.Notarization;
using Ows.Core.Reporting;
using Ows.Core.Verification;


namespace Ows.Verifier.Server;

/// <summary>
/// Provides route endpoint mapping extension methods for OWS package submissions, uploads, verification queueing, and report queries.
/// </summary>
internal static class VerifierPackageEndpoints
{
    /// <summary>
    /// Maps the packages endpoints (registering metadata, uploading files, verification status, and text reports).
    /// </summary>
    /// <param name="app">The route builder application instance.</param>
    /// <returns>The route builder with endpoints mapped.</returns>
    public static void MapVerifierPackageEndpoints(this IEndpointRouteBuilder app)
    {
        var auditLogger = app.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit");

        app.MapPost("/packages", async (HttpRequest request, HttpContext context, IPackageSubmissionStore packageStore,
            IVerifierStorage storage, IVerifierAuditStore auditStore, IPackageBlobStore blobStore,
            IPackageVerificationJobStore jobStore, CancellationToken cancellationToken) =>
        {
            if (request.HasFormContentType)
            {
                return await HandlePackageUploadAsync(
                    request,
                    context,
                    packageStore,
                    storage,
                    auditStore,
                    blobStore,
                    jobStore,
                    cancellationToken);
            }

            var idempotencyKey = request.Headers["Idempotency-Key"].FirstOrDefault();
            VerifierPackageSubmissionRequest jsonRequest;
            try
            {
                jsonRequest = await request.ReadFromJsonAsync<VerifierPackageSubmissionRequest>(cancellationToken)
                              ?? throw new JsonException("Request body deserialized to null.");
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Invalid JSON payload: {ex.Message}");
            }

            jsonRequest = jsonRequest with { IdempotencyKey = idempotencyKey };
            var validationError = jsonRequest.GetValidationError();
            if (validationError is not null)
            {
                return Results.BadRequest(validationError);
            }

            var derivedContext = await ResolvePackageContextFromSessionAsync(
                storage,
                jsonRequest.SessionId,
                jsonRequest.InstitutionId,
                jsonRequest.AssessmentId,
                jsonRequest.StudentUserId,
                cancellationToken);

            jsonRequest = jsonRequest with
            {
                InstitutionId = derivedContext.InstitutionId,
                AssessmentId = derivedContext.AssessmentId,
                StudentUserId = derivedContext.StudentUserId
            };

            var access = VerifierAuthorizationHelpers.TryGetAccessContext(context);
            if (access is not null && !VerifierRolePolicy.IsOperatorRole(access.Role))
            {
                if (string.IsNullOrWhiteSpace(jsonRequest.InstitutionId) ||
                    !string.Equals(jsonRequest.InstitutionId, access.InstitutionId, StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = $"{access.Role} may only submit packages for their own institution." },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                if (VerifierRolePolicy.IsStudentClientRole(access.Role) &&
                    !string.IsNullOrWhiteSpace(access.StudentUserId))
                {
                    if (string.IsNullOrWhiteSpace(jsonRequest.StudentUserId) ||
                        !string.Equals(jsonRequest.StudentUserId, access.StudentUserId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.Json(
                            new { error = "StudentClient bound key must match the student user ID of the package." },
                            statusCode: StatusCodes.Status403Forbidden);
                    }
                }
            }

            try
            {
                var response = await packageStore.SubmitAsync(jsonRequest, cancellationToken);
                await VerifierAuditHelpers.WriteAuditEventAsync(
                    auditStore,
                    auditLogger,
                    context,
                    eventType: "package.submitted",
                    result: "Registered",
                    access: VerifierAuthorizationHelpers.TryGetAccessContext(context),
                    institutionId: response.InstitutionId,
                    sessionId: response.SessionId,
                    packageId: response.SubmissionId,
                    assessmentId: response.AssessmentId,
                    metadata: VerifierAuditHelpers.CreateMetadata(
                        ("storageProvider", response.ObjectStorageProvider),
                        ("objectBucket", response.ObjectBucket),
                        ("objectKey", response.ObjectKey),
                        ("idempotencyKey", response.IdempotencyKey)),
                    cancellationToken: cancellationToken);
                return Results.Ok(response);
            }
            catch (NotSupportedException exception)
            {
                return Results.BadRequest(exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                if (exception.Message.Contains("idempotency key already exists", StringComparison.OrdinalIgnoreCase) ||
                    exception.Message.Contains("already registered with different metadata",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Conflict(exception.Message);
                }

                return Results.BadRequest(exception.Message);
            }
        });

        app.MapPost("/packages/upload", async (HttpRequest request, HttpContext context,
                IPackageSubmissionStore packageStore,
                IVerifierStorage storage, IVerifierAuditStore auditStore, IPackageBlobStore blobStore,
                IPackageVerificationJobStore jobStore, CancellationToken cancellationToken) =>
            await HandlePackageUploadAsync(
                request,
                context,
                packageStore,
                storage,
                auditStore,
                blobStore,
                jobStore,
                cancellationToken));

        app.MapPut("/packages/{id}", async (string id, HttpRequest request, HttpContext context,
            IPackageSubmissionStore packageStore, IVerifierAuditStore auditStore, IPackageBlobStore blobStore,
            IPackageVerificationJobStore jobStore, CancellationToken cancellationToken) =>
        {
            var submission = await packageStore.GetAsync(id, cancellationToken);
            if (submission is null)
            {
                return Results.NotFound("Unknown package submission.");
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest("A file upload is required.");
            }

            var form = await request.ReadFormAsync(cancellationToken);
            if (form.Files.GetFile("file") is not { } file)
            {
                return Results.BadRequest("A file upload is required.");
            }

            var extension = Path.GetExtension(file.FileName);
            if (!string.Equals(extension, ".owspkg", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("Only .owspkg files are accepted.");
            }

            await VerifierAuditHelpers.WriteAuditEventAsync(
                auditStore,
                auditLogger,
                context,
                eventType: "package.upload.started",
                result: "Started",
                access: VerifierAuthorizationHelpers.TryGetAccessContext(context),
                institutionId: submission.InstitutionId,
                sessionId: submission.SessionId,
                packageId: submission.SubmissionId,
                assessmentId: submission.AssessmentId,
                cancellationToken: cancellationToken);

            try
            {
                await using var source = file.OpenReadStream();
                var savedBlob = await blobStore.SaveAsync(source, cancellationToken);
                if (!string.Equals(savedBlob.PackageSha256, submission.PackageSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    await VerifierAuditHelpers.WriteAuditEventAsync(
                        auditStore,
                        auditLogger,
                        context,
                        eventType: "package.upload.failed",
                        result: "HashMismatch",
                        access: VerifierAuthorizationHelpers.TryGetAccessContext(context),
                        institutionId: submission.InstitutionId,
                        sessionId: submission.SessionId,
                        packageId: submission.SubmissionId,
                        assessmentId: submission.AssessmentId,
                        metadata: VerifierAuditHelpers.CreateMetadata(("packageSha256", savedBlob.PackageSha256)),
                        cancellationToken: cancellationToken);
                    return Results.BadRequest(
                        "Uploaded package hash does not match the registered package SHA-256 metadata.");
                }

                var job = await QueuePackageVerificationAsync(
                    submission.SubmissionId,
                    submission,
                    VerifierAuthorizationHelpers.TryGetAccessContext(context),
                    packageStore,
                    jobStore,
                    auditStore,
                    context,
                    cancellationToken);

                await VerifierAuditHelpers.WriteAuditEventAsync(
                    auditStore,
                    auditLogger,
                    context,
                    eventType: "package.upload.completed",
                    result: "Stored",
                    access: VerifierAuthorizationHelpers.TryGetAccessContext(context),
                    institutionId: submission.InstitutionId,
                    sessionId: submission.SessionId,
                    packageId: submission.SubmissionId,
                    assessmentId: submission.AssessmentId,
                    metadata: VerifierAuditHelpers.CreateMetadata(("packageSha256", savedBlob.PackageSha256)),
                    cancellationToken: cancellationToken);

                return Results.Ok(new
                {
                    submissionId = submission.SubmissionId,
                    sessionId = submission.SessionId,
                    packageSha256 = savedBlob.PackageSha256,
                    verificationStatus = "Pending",
                    verificationJobId = job.Id
                });
            }
            catch (InvalidOperationException exception)
            {
                await VerifierAuditHelpers.WriteAuditEventAsync(
                    auditStore,
                    auditLogger,
                    context,
                    eventType: "package.upload.failed",
                    result: "Rejected",
                    access: VerifierAuthorizationHelpers.TryGetAccessContext(context),
                    institutionId: submission.InstitutionId,
                    sessionId: submission.SessionId,
                    packageId: submission.SubmissionId,
                    assessmentId: submission.AssessmentId,
                    metadata: VerifierAuditHelpers.CreateMetadata(("error", exception.Message)),
                    cancellationToken: cancellationToken);
                return Results.BadRequest(exception.Message);
            }
        });

        app.MapPost("/packages/{id}/verify", async (string id, HttpContext context,
            IPackageSubmissionStore packageStore,
            IVerifierAuditStore auditStore, IPackageBlobStore blobStore, IPackageVerificationJobStore jobStore,
            CancellationToken cancellationToken) =>
        {
            var submission = await packageStore.GetAsync(id, cancellationToken);
            if (submission is null)
            {
                return Results.NotFound("Unknown package submission.");
            }

            if (!await blobStore.ExistsAsync(submission.ObjectKey, cancellationToken))
            {
                return Results.BadRequest(
                    "Package bytes are not available for verification. Please upload the package file.");
            }

            var job = await QueuePackageVerificationAsync(
                submission.SubmissionId,
                submission,
                VerifierAuthorizationHelpers.TryGetAccessContext(context),
                packageStore,
                jobStore,
                auditStore,
                context,
                cancellationToken);
            return Results.Ok(new
            {
                submissionId = submission.SubmissionId,
                verificationStatus = "Pending",
                verificationJobId = job.Id
            });
        });

        app.MapGet("/packages/{id}", async (string id, IPackageSubmissionStore packageStore,
            IPackageBlobStore blobStore, CancellationToken cancellationToken) =>
        {
            try
            {
                var submission = await packageStore.GetAsync(id, cancellationToken);
                if (submission is null)
                {
                    return Results.NotFound("Unknown package submission.");
                }

                return Results.Ok(new
                {
                    submission.SubmissionId,
                    submission.SessionId,
                    submission.InstitutionId,
                    submission.AssessmentId,
                    submission.StudentUserId,
                    submission.IdempotencyKey,
                    submission.ObjectStorageProvider,
                    submission.ObjectBucket,
                    submission.ObjectKey,
                    submission.PackageSha256,
                    submission.PackageSizeBytes,
                    submission.SessionHeadReceiptHash,
                    submission.SessionHeadEventHash,
                    submission.SessionCheckpointCount,
                    submission.VerificationStatus,
                    submission.VerificationJobId,
                    submission.TrustStatus,
                    submission.LastVerificationError,
                    submission.CreatedAtUtc,
                    blobAvailable = await blobStore.ExistsAsync(submission.ObjectKey, cancellationToken)
                });
            }
            catch (NotSupportedException exception)
            {
                return Results.BadRequest(exception.Message);
            }
        });

        app.MapGet("/packages/{id}/verification", async (string id, HttpContext context,
            IPackageSubmissionStore packageStore,
            IVerifierAuditStore auditStore, CancellationToken cancellationToken) =>
        {
            var submission = await packageStore.GetAsync(id, cancellationToken);
            if (submission is null)
            {
                return Results.NotFound("Unknown package submission.");
            }

            if (string.IsNullOrWhiteSpace(submission.VerificationResultJson))
            {
                return Results.Json(
                    new
                    {
                        status = submission.VerificationStatus,
                        error = submission.LastVerificationError
                    },
                    statusCode: submission.VerificationStatus == "Failed"
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status404NotFound);
            }

            await VerifierAuditHelpers.WriteAuditEventAsync(
                auditStore,
                auditLogger,
                context,
                eventType: "report.read",
                result: "Returned",
                access: VerifierAuthorizationHelpers.TryGetAccessContext(context),
                institutionId: submission.InstitutionId,
                sessionId: submission.SessionId,
                packageId: submission.SubmissionId,
                assessmentId: submission.AssessmentId,
                metadata: VerifierAuditHelpers.CreateMetadata(("contentType", "application/json")),
                cancellationToken: cancellationToken);

            return Results.Content(submission.VerificationResultJson, "application/json");
        });

        app.MapGet("/packages/{id}/report", async (string id, HttpContext context, IPackageSubmissionStore packageStore,
            IVerifierAuditStore auditStore, IReportGenerator reportGenerator, CancellationToken cancellationToken) =>
        {
            var submission = await packageStore.GetAsync(id, cancellationToken);
            if (submission is null)
            {
                return Results.NotFound("Unknown package submission.");
            }

            if (string.IsNullOrWhiteSpace(submission.VerificationResultJson))
            {
                return Results.Json(
                    new
                    {
                        status = submission.VerificationStatus,
                        error = submission.LastVerificationError
                    },
                    statusCode: submission.VerificationStatus == "Failed"
                        ? StatusCodes.Status409Conflict
                        : StatusCodes.Status404NotFound);
            }

            var verificationResult = JsonSerializer.Deserialize<VerificationResult>(
                submission.VerificationResultJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (verificationResult is null)
            {
                return Results.Problem("Stored verification result is invalid.");
            }

            var report = await reportGenerator.GenerateAsync(
                new ReportRequest
                {
                    VerificationResult = verificationResult,
                    Format = ReportFormat.Text
                },
                cancellationToken);

            await VerifierAuditHelpers.WriteAuditEventAsync(
                auditStore,
                auditLogger,
                context,
                eventType: "report.read",
                result: "Returned",
                access: VerifierAuthorizationHelpers.TryGetAccessContext(context),
                institutionId: submission.InstitutionId,
                sessionId: submission.SessionId,
                packageId: submission.SubmissionId,
                assessmentId: submission.AssessmentId,
                metadata: VerifierAuditHelpers.CreateMetadata(("contentType", "text/plain")),
                cancellationToken: cancellationToken);

            return Results.Text(report.Content, "text/plain");
        });
    }

    /// <summary>
    /// Handles a multipart file upload for an .owspkg, stores it in the blob storage, and queues the verification job.
    /// </summary>
    private static async Task<IResult> HandlePackageUploadAsync(
        HttpRequest request,
        HttpContext context,
        IPackageSubmissionStore packageStore,
        IVerifierStorage storage,
        IVerifierAuditStore auditStore,
        IPackageBlobStore blobStore,
        IPackageVerificationJobStore jobStore,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest("A file upload is required.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        if (form.Files.GetFile("file") is not { } file)
        {
            return Results.BadRequest("A file upload is required.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".owspkg", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest("Only .owspkg files are accepted.");
        }

        var access = VerifierAuthorizationHelpers.TryGetAccessContext(context);
        var idempotencyKey = request.Headers["Idempotency-Key"].FirstOrDefault();
        var sessionId = form["sessionId"].FirstOrDefault();
        var institutionId = form["institutionId"].FirstOrDefault();
        var assessmentId = form["assessmentId"].FirstOrDefault();
        var studentUserId = form["studentUserId"].FirstOrDefault();

        await VerifierAuditHelpers.WriteAuditEventAsync(
            auditStore,
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit"),
            context,
            eventType: "package.upload.started",
            result: "Started",
            access: access,
            institutionId: institutionId,
            sessionId: sessionId,
            assessmentId: assessmentId,
            cancellationToken: cancellationToken);

        try
        {
            await using var source = file.OpenReadStream();
            var savedBlob = await blobStore.SaveAsync(source, cancellationToken);
            var derivedContext = await ResolvePackageContextFromSessionAsync(
                storage,
                sessionId,
                institutionId,
                assessmentId,
                studentUserId,
                cancellationToken);

            if (access is not null && !VerifierRolePolicy.IsOperatorRole(access.Role))
            {
                if (string.IsNullOrWhiteSpace(derivedContext.InstitutionId) ||
                    !string.Equals(derivedContext.InstitutionId, access.InstitutionId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Json(
                        new { error = $"{access.Role} may only upload packages for their own institution." },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                if (VerifierRolePolicy.IsStudentClientRole(access.Role) &&
                    !string.IsNullOrWhiteSpace(access.StudentUserId))
                {
                    if (string.IsNullOrWhiteSpace(derivedContext.StudentUserId) ||
                        !string.Equals(derivedContext.StudentUserId, access.StudentUserId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.Json(
                            new { error = "StudentClient bound key must match the student user ID of the package." },
                            statusCode: StatusCodes.Status403Forbidden);
                    }
                }
            }

            var submissionRequest = new VerifierPackageSubmissionRequest
            {
                SessionId = sessionId,
                InstitutionId = derivedContext.InstitutionId,
                AssessmentId = derivedContext.AssessmentId,
                StudentUserId = derivedContext.StudentUserId,
                IdempotencyKey = idempotencyKey,
                ObjectStorageProvider = "local",
                ObjectBucket = "packages",
                ObjectKey = savedBlob.ObjectKey,
                PackageSha256 = savedBlob.PackageSha256,
                PackageSizeBytes = savedBlob.PackageSizeBytes
            };

            VerifierPackageSubmissionResponse submission;
            try
            {
                submission = await packageStore.SubmitAsync(submissionRequest, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                if (exception.Message.Contains("idempotency key already exists", StringComparison.OrdinalIgnoreCase) ||
                    exception.Message.Contains("already registered with different metadata",
                        StringComparison.OrdinalIgnoreCase))
                {
                    await VerifierAuditHelpers.WriteAuditEventAsync(
                        auditStore,
                        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit"),
                        context,
                        eventType: "package.upload.failed",
                        result: "Conflict",
                        access: access,
                        institutionId: derivedContext.InstitutionId,
                        sessionId: sessionId,
                        assessmentId: derivedContext.AssessmentId,
                        metadata: VerifierAuditHelpers.CreateMetadata(("error", exception.Message)),
                        cancellationToken: cancellationToken);
                    return Results.Conflict(exception.Message);
                }

                throw;
            }

            if (submission.VerificationStatus is "Pending" or "Running" or "Completed" &&
                !string.IsNullOrWhiteSpace(submission.VerificationJobId))
            {
                return Results.Ok(new
                {
                    submissionId = submission.SubmissionId,
                    sessionId = submission.SessionId,
                    packageSha256 = submission.PackageSha256,
                    verificationStatus = submission.VerificationStatus,
                    verificationJobId = submission.VerificationJobId,
                    trustStatus = submission.TrustStatus
                });
            }

            PackageVerificationJobRecord job;
            try
            {
                job = await QueuePackageVerificationAsync(
                    submission.SubmissionId,
                    submission,
                    access,
                    packageStore,
                    jobStore,
                    auditStore,
                    context,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                await packageStore.UpdateVerificationStateAsync(
                    submission.SubmissionId,
                    "Failed",
                    null,
                    null,
                    null,
                    "Package uploaded but verification job queue failed.",
                    cancellationToken);
                await VerifierAuditHelpers.WriteAuditEventAsync(
                    auditStore,
                    context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit"),
                    context,
                    eventType: "package.upload.failed",
                    result: "JobQueueFailed",
                    access: access,
                    institutionId: submission.InstitutionId,
                    sessionId: submission.SessionId,
                    packageId: submission.SubmissionId,
                    assessmentId: submission.AssessmentId,
                    metadata: VerifierAuditHelpers.CreateMetadata(("error", exception.Message)),
                    cancellationToken: cancellationToken);
                return Results.Problem("Package was uploaded but verification job creation failed.");
            }

            await VerifierAuditHelpers.WriteAuditEventAsync(
                auditStore,
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit"),
                context,
                eventType: "package.upload.completed",
                result: "Stored",
                access: access,
                institutionId: submission.InstitutionId,
                sessionId: submission.SessionId,
                packageId: submission.SubmissionId,
                assessmentId: submission.AssessmentId,
                metadata: VerifierAuditHelpers.CreateMetadata(("packageSha256", submission.PackageSha256)),
                cancellationToken: cancellationToken);
            await VerifierAuditHelpers.WriteAuditEventAsync(
                auditStore,
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit"),
                context,
                eventType: "package.submitted",
                result: "Stored",
                access: access,
                institutionId: submission.InstitutionId,
                sessionId: submission.SessionId,
                packageId: submission.SubmissionId,
                assessmentId: submission.AssessmentId,
                metadata: VerifierAuditHelpers.CreateMetadata(
                    ("storageProvider", submission.ObjectStorageProvider),
                    ("objectBucket", submission.ObjectBucket),
                    ("objectKey", submission.ObjectKey)),
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                submissionId = submission.SubmissionId,
                sessionId = submission.SessionId,
                packageSha256 = submission.PackageSha256,
                verificationStatus = "Pending",
                verificationJobId = job.Id
            });
        }
        catch (InvalidOperationException exception)
        {
            await VerifierAuditHelpers.WriteAuditEventAsync(
                auditStore,
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit"),
                context,
                eventType: "package.upload.failed",
                result: "Rejected",
                access: access,
                institutionId: institutionId,
                sessionId: sessionId,
                assessmentId: assessmentId,
                metadata: VerifierAuditHelpers.CreateMetadata(("error", exception.Message)),
                cancellationToken: cancellationToken);
            return Results.BadRequest(exception.Message);
        }
    }

    /// <summary>
    /// Queues a package verification job and mirrors the job details to the submission record.
    /// </summary>
    private static async Task<PackageVerificationJobRecord> QueuePackageVerificationAsync(
        string packageId,
        VerifierPackageSubmissionResponse submission,
        VerifierAccessContext? access,
        IPackageSubmissionStore packageStore,
        IPackageVerificationJobStore jobStore,
        IVerifierAuditStore auditStore,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var job = await jobStore.QueueAsync(packageId, access?.KeyId, cancellationToken);
        await packageStore.UpdateVerificationStateAsync(
            packageId,
            job.Status == "Running" ? "Running" : "Pending",
            job.Id,
            null,
            null,
            null,
            cancellationToken);
        await VerifierAuditHelpers.WriteAuditEventAsync(
            auditStore,
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit"),
            context,
            eventType: "package.verification.queued",
            result: job.Status,
            access: access,
            institutionId: submission.InstitutionId,
            sessionId: submission.SessionId,
            packageId: submission.SubmissionId,
            assessmentId: submission.AssessmentId,
            metadata: VerifierAuditHelpers.CreateMetadata(("jobId", job.Id)),
            cancellationToken: cancellationToken);
        return job;
    }

    /// <summary>
    /// Fallback resolution of institution/student contexts using the session's metadata.
    /// </summary>
    private static async Task<(string? InstitutionId, string? AssessmentId, string? StudentUserId)>
        ResolvePackageContextFromSessionAsync(
            IVerifierStorage storage,
            string? sessionId,
            string? institutionId,
            string? assessmentId,
            string? studentUserId,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return (institutionId, assessmentId, studentUserId);
        }

        try
        {
            var session = await storage.GetSessionAsync(new AssessmentSessionId(sessionId), cancellationToken);
            return (
                institutionId ??
                VerifierAuthorizationHelpers.TryGetMetadataValue(session.MetadataJson, "institutionId"),
                assessmentId ?? session.AssessmentId ??
                VerifierAuthorizationHelpers.TryGetMetadataValue(session.MetadataJson, "assessmentId"),
                studentUserId ??
                VerifierAuthorizationHelpers.TryGetMetadataValue(session.MetadataJson, "studentUserId"));
        }
        catch (InvalidOperationException)
        {
            return (institutionId, assessmentId, studentUserId);
        }
    }
}