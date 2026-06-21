using Ows.Core.Education;

namespace Ows.Verifier.Server;

/// <summary>
/// Provides route endpoint mapping extension methods for OWS educational entity CRUD operations.
/// </summary>
internal static class VerifierEducationEndpoints {
    /// <summary>
    /// Maps the education CRUD endpoints (institutions, courses, assessments, class groups, offerings, enrollments, users).
    /// </summary>
    /// <param name="app">The route builder application instance.</param>
    /// <returns>The route builder with endpoints mapped.</returns>
    public static void MapVerifierEducationEndpoints(this IEndpointRouteBuilder app) {
        var auditLogger = app.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Ows.Verifier.Audit");

        app.MapPost("/education/institutions", async (HttpContext context, Institution institution,
            IEducationStore educationStore, IVerifierAuditStore auditStore, CancellationToken cancellationToken) => {
                var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
                if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role) &&
                    !string.Equals(institution.Id.Value, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase)) {
                    await WriteDeniedAuditAsync(
                        context,
                        auditStore,
                        auditLogger,
                        callerAccess,
                        institution.Id.Value,
                        "institution",
                        institution.Id.Value,
                        cancellationToken);
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                await educationStore.CreateInstitutionAsync(institution, cancellationToken);
            await WriteEducationAuditAsync(
                context,
                auditStore,
                auditLogger,
                "education.institution.created",
                callerAccess,
                institution.Id.Value,
                "institution",
                institution.Id.Value,
                "Created",
                "create",
                cancellationToken);
                return Results.Ok(institution);
            }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationWritePolicy);

        app.MapGet("/education/institutions/{id}", async (string id, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
            var institution = await educationStore.GetInstitutionAsync(new InstitutionId(id), cancellationToken);
            return institution is null ? Results.NotFound($"Institution '{id}' not found.") : Results.Ok(institution);
        }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationReadPolicy);

        app.MapPost("/education/courses", async (HttpContext context, Course course, IEducationStore educationStore,
            IVerifierAuditStore auditStore, CancellationToken cancellationToken) => {
            var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
            if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role) &&
                !string.Equals(course.InstitutionId.Value, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase)) {
                await WriteDeniedAuditAsync(
                    context,
                    auditStore,
                    auditLogger,
                    callerAccess,
                    course.InstitutionId.Value,
                    "course",
                    course.Id.Value,
                    cancellationToken);
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            await educationStore.CreateCourseAsync(course, cancellationToken);
            await WriteEducationAuditAsync(
                context,
                auditStore,
                auditLogger,
                "education.course.created",
                callerAccess,
                course.InstitutionId.Value,
                "course",
                course.Id.Value,
                "Created",
                "create",
                cancellationToken);
            return Results.Ok(course);
        }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationWritePolicy);

        app.MapGet("/education/courses/{id}", async (string id, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
            var course = await educationStore.GetCourseAsync(new CourseId(id), cancellationToken);
            return course is null ? Results.NotFound($"Course '{id}' not found.") : Results.Ok(course);
        }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationReadPolicy);

        app.MapPost("/education/class-groups", async (HttpContext context, ClassGroup classGroup,
            IEducationStore educationStore, IVerifierAuditStore auditStore, CancellationToken cancellationToken) => {
            var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
            if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role) &&
                !string.Equals(classGroup.InstitutionId.Value, callerAccess.InstitutionId,
                    StringComparison.OrdinalIgnoreCase)) {
                await WriteDeniedAuditAsync(
                    context,
                    auditStore,
                    auditLogger,
                    callerAccess,
                    classGroup.InstitutionId.Value,
                    "class_group",
                    classGroup.Id.Value,
                    cancellationToken);
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            await educationStore.CreateClassGroupAsync(classGroup, cancellationToken);
            await WriteEducationAuditAsync(
                context,
                auditStore,
                auditLogger,
                "education.class_group.created",
                callerAccess,
                classGroup.InstitutionId.Value,
                "class_group",
                classGroup.Id.Value,
                "Created",
                "create",
                cancellationToken);
            return Results.Ok(classGroup);
        }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationWritePolicy);

        app.MapGet("/education/class-groups/{id}", async (string id, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
            var classGroup = await educationStore.GetClassGroupAsync(new ClassGroupId(id), cancellationToken);
            return classGroup is null ? Results.NotFound($"Class group '{id}' not found.") : Results.Ok(classGroup);
        }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationReadPolicy);

        app.MapPost("/education/course-offerings", async (HttpContext context, CourseOffering offering,
            IEducationStore educationStore, IVerifierAuditStore auditStore, CancellationToken cancellationToken) => {
            var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
            if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role) &&
                !string.Equals(offering.InstitutionId.Value, callerAccess.InstitutionId,
                    StringComparison.OrdinalIgnoreCase)) {
                await WriteDeniedAuditAsync(
                    context,
                    auditStore,
                    auditLogger,
                    callerAccess,
                    offering.InstitutionId.Value,
                    "course_offering",
                    offering.Id.Value,
                    cancellationToken);
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            await educationStore.CreateCourseOfferingAsync(offering, cancellationToken);
            await WriteEducationAuditAsync(
                context,
                auditStore,
                auditLogger,
                "education.course_offering.created",
                callerAccess,
                offering.InstitutionId.Value,
                "course_offering",
                offering.Id.Value,
                "Created",
                "create",
                cancellationToken);
            return Results.Ok(offering);
        }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationWritePolicy);

        app.MapGet("/education/course-offerings/{id}", async (string id, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
            var offering = await educationStore.GetCourseOfferingAsync(new CourseOfferingId(id), cancellationToken);
            return offering is null ? Results.NotFound($"Course offering '{id}' not found.") : Results.Ok(offering);
        }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationReadPolicy);

        app.MapPost("/education/enrollments", async (HttpContext context, StudentEnrollment studentEnrollment,
            IEducationStore educationStore, IVerifierAuditStore auditStore, CancellationToken cancellationToken) => {
            var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
            string? institutionId = null;
            if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role)) {
                var offering =
                    await educationStore.GetCourseOfferingAsync(studentEnrollment.CourseOfferingId, cancellationToken);
                institutionId = offering?.InstitutionId.Value;
                if (offering is null || !string.Equals(offering.InstitutionId.Value, callerAccess.InstitutionId,
                        StringComparison.OrdinalIgnoreCase)) {
                    await WriteDeniedAuditAsync(
                        context,
                        auditStore,
                        auditLogger,
                        callerAccess,
                        institutionId ?? callerAccess.InstitutionId,
                        "enrollment",
                        studentEnrollment.Id.Value,
                        cancellationToken);
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
            }

            await educationStore.CreateStudentEnrollmentAsync(studentEnrollment, cancellationToken);
            await WriteEducationAuditAsync(
                context,
                auditStore,
                auditLogger,
                "education.enrollment.created",
                callerAccess,
                institutionId,
                "enrollment",
                studentEnrollment.Id.Value,
                "Created",
                "create",
                cancellationToken,
                ("courseOfferingId", studentEnrollment.CourseOfferingId.Value),
                ("studentUserId", studentEnrollment.StudentUserId.Value));
            return Results.Ok(studentEnrollment);
        }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationWritePolicy);

        app.MapGet("/education/enrollments/student/{studentUserId}", async (HttpContext context, string studentUserId,
            IEducationStore educationStore, IVerifierAuditStore auditStore, CancellationToken cancellationToken) => {
            var studentEnrollments = await educationStore.GetStudentEnrollmentsForStudentAsync(
                new UserId(studentUserId),
                cancellationToken);
            await WriteEducationAuditAsync(
                context,
                auditStore,
                auditLogger,
                "education.enrollments.by_student.read",
                VerifierAuthorizationHelpers.TryGetAccessContext(context),
                null,
                "enrollment_read",
                studentUserId,
                "Returned",
                "read",
                cancellationToken,
                ("studentUserId", studentUserId));
            return Results.Ok(studentEnrollments);
        }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationReadPolicy);

        app.MapGet("/education/enrollments/offering/{offeringId}", async (HttpContext context, string offeringId,
            IEducationStore educationStore, IVerifierAuditStore auditStore, CancellationToken cancellationToken) => {
            var studentEnrollments = await educationStore.GetStudentEnrollmentsForOfferingAsync(
                new CourseOfferingId(offeringId), cancellationToken);
            await WriteEducationAuditAsync(
                context,
                auditStore,
                auditLogger,
                "education.enrollments.by_offering.read",
                VerifierAuthorizationHelpers.TryGetAccessContext(context),
                null,
                "enrollment_read",
                offeringId,
                "Returned",
                "read",
                cancellationToken,
                ("offeringId", offeringId));
            return Results.Ok(studentEnrollments);
        }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationReadPolicy);

        app.MapPost("/education/assessments", async (HttpContext context, Assessment assessment,
            IEducationStore educationStore, IVerifierAuditStore auditStore, CancellationToken cancellationToken) => {
            var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
            if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role) &&
                !string.Equals(assessment.InstitutionId.Value, callerAccess.InstitutionId,
                    StringComparison.OrdinalIgnoreCase)) {
                await WriteDeniedAuditAsync(
                    context,
                    auditStore,
                    auditLogger,
                    callerAccess,
                    assessment.InstitutionId.Value,
                    "assessment",
                    assessment.Id.Value,
                    cancellationToken);
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            await educationStore.CreateAssessmentAsync(assessment, cancellationToken);
            await WriteEducationAuditAsync(
                context,
                auditStore,
                auditLogger,
                "education.assessment.created",
                callerAccess,
                assessment.InstitutionId.Value,
                "assessment",
                assessment.Id.Value,
                "Created",
                "create",
                cancellationToken);
            return Results.Ok(assessment);
        }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationWritePolicy);

        app.MapGet("/education/assessments/{id}", async (string id, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
            var assessment = await educationStore.GetAssessmentAsync(new AssessmentId(id), cancellationToken);
            return assessment is null ? Results.NotFound($"Assessment '{id}' not found.") : Results.Ok(assessment);
        }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationReadPolicy);

        app.MapPost("/education/users", async (HttpContext context, User user, IEducationStore educationStore,
            IVerifierAuditStore auditStore, CancellationToken cancellationToken) => {
            var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
            if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role) &&
                !string.Equals(user.InstitutionId.Value, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase)) {
                await WriteDeniedAuditAsync(
                    context,
                    auditStore,
                    auditLogger,
                    callerAccess,
                    user.InstitutionId.Value,
                    "user",
                    user.Id.Value,
                    cancellationToken);
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            await educationStore.CreateUserAsync(user, cancellationToken);
            await WriteEducationAuditAsync(
                context,
                auditStore,
                auditLogger,
                "education.user.created",
                callerAccess,
                user.InstitutionId.Value,
                "user",
                user.Id.Value,
                "Created",
                "create",
                cancellationToken);
            return Results.Ok(user);
        }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationWritePolicy);

        app.MapGet("/education/users/{id}", async (string id, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
            var user = await educationStore.GetUserAsync(new UserId(id), cancellationToken);
            return user is null ? Results.NotFound($"User '{id}' not found.") : Results.Ok(user);
        }).RequireRateLimiting(VerifierRateLimitingRegistration.EducationReadPolicy);
    }

    private static Task WriteEducationAuditAsync(
        HttpContext context,
        IVerifierAuditStore auditStore,
        ILogger auditLogger,
        string eventType,
        VerifierAccessContext? access,
        string? institutionId,
        string resourceType,
        string resourceId,
        string result,
        string operation,
        CancellationToken cancellationToken,
        params (string Key, string? Value)[] metadata) =>
        VerifierAuditHelpers.WriteAuditEventAsync(
            auditStore,
            auditLogger,
            context,
            eventType: eventType,
            result: result,
            access: access,
            institutionId: institutionId,
            metadata: VerifierAuditHelpers.CreateMetadata(
                [("resourceType", resourceType), ("resourceId", resourceId), ("operation", operation), .. metadata]),
            cancellationToken: cancellationToken);

    private static Task WriteDeniedAuditAsync(
        HttpContext context,
        IVerifierAuditStore auditStore,
        ILogger auditLogger,
        VerifierAccessContext? access,
        string? institutionId,
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken) =>
        VerifierAuditHelpers.WriteAuditEventAsync(
            auditStore,
            auditLogger,
            context,
            eventType: "access.denied",
            result: "Forbidden",
            access: access,
            institutionId: institutionId,
            metadata: VerifierAuditHelpers.CreateMetadata(
                ("endpoint", context.Request.Path.Value),
                ("method", context.Request.Method),
                ("resourceType", resourceType),
                ("resourceId", resourceId)),
            cancellationToken: cancellationToken);
}
