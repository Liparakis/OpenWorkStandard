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
        // Institutions
        app.MapPost("/education/institutions", async (HttpContext context, Institution institution,
            IEducationStore educationStore,
            CancellationToken cancellationToken) => {
                var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
                if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role)) {
                    if (!string.Equals(institution.Id.Value, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase)) {
                        return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }
                }

                await educationStore.CreateInstitutionAsync(institution, cancellationToken);
                return Results.Ok(institution);
            });

        app.MapGet("/education/institutions/{id}", async (string id, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
                var institution = await educationStore.GetInstitutionAsync(new InstitutionId(id), cancellationToken);
                return institution is null ? Results.NotFound($"Institution '{id}' not found.") : Results.Ok(institution);
            });

        // Courses
        app.MapPost("/education/courses", async (HttpContext context, Course course, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
                var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
                if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role)) {
                    if (!string.Equals(course.InstitutionId.Value, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase)) {
                        return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }
                }

                await educationStore.CreateCourseAsync(course, cancellationToken);
                return Results.Ok(course);
            });

        app.MapGet("/education/courses/{id}", async (string id, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
                var course = await educationStore.GetCourseAsync(new CourseId(id), cancellationToken);
                return course is null ? Results.NotFound($"Course '{id}' not found.") : Results.Ok(course);
            });

        // Class groups
        app.MapPost("/education/class-groups", async (HttpContext context, ClassGroup classGroup,
            IEducationStore educationStore,
            CancellationToken cancellationToken) => {
                var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
                if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role)) {
                    if (!string.Equals(classGroup.InstitutionId.Value, callerAccess.InstitutionId,
                            StringComparison.OrdinalIgnoreCase)) {
                        return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }
                }

                await educationStore.CreateClassGroupAsync(classGroup, cancellationToken);
                return Results.Ok(classGroup);
            });

        app.MapGet("/education/class-groups/{id}", async (string id, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
                var classGroup = await educationStore.GetClassGroupAsync(new ClassGroupId(id), cancellationToken);
                return classGroup is null ? Results.NotFound($"Class group '{id}' not found.") : Results.Ok(classGroup);
            });

        // Course offerings
        app.MapPost("/education/course-offerings", async (HttpContext context, CourseOffering offering,
            IEducationStore educationStore,
            CancellationToken cancellationToken) => {
                var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
                if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role)) {
                    if (!string.Equals(offering.InstitutionId.Value, callerAccess.InstitutionId,
                            StringComparison.OrdinalIgnoreCase)) {
                        return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }
                }

                await educationStore.CreateCourseOfferingAsync(offering, cancellationToken);
                return Results.Ok(offering);
            });

        app.MapGet("/education/course-offerings/{id}", async (string id, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
                var offering = await educationStore.GetCourseOfferingAsync(new CourseOfferingId(id), cancellationToken);
                return offering is null ? Results.NotFound($"Course offering '{id}' not found.") : Results.Ok(offering);
            });

        // Student enrollments
        app.MapPost("/education/enrollments", async (HttpContext context, StudentEnrollment studentEnrollment,
            IEducationStore educationStore,
            CancellationToken cancellationToken) => {
                var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
                if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role)) {
                    var offering =
                        await educationStore.GetCourseOfferingAsync(studentEnrollment.CourseOfferingId, cancellationToken);
                    if (offering is null || !string.Equals(offering.InstitutionId.Value, callerAccess.InstitutionId,
                            StringComparison.OrdinalIgnoreCase)) {
                        return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }
                }

                await educationStore.CreateStudentEnrollmentAsync(studentEnrollment, cancellationToken);
                return Results.Ok(studentEnrollment);
            });

        app.MapGet("/education/enrollments/student/{studentUserId}", async (string studentUserId,
            IEducationStore educationStore,
            CancellationToken cancellationToken) => {
                var studentEnrollments = await educationStore.GetStudentEnrollmentsForStudentAsync(
                    new UserId(studentUserId),
                    cancellationToken);
                return Results.Ok(studentEnrollments);
            });

        app.MapGet("/education/enrollments/offering/{offeringId}", async (string offeringId,
            IEducationStore educationStore, CancellationToken cancellationToken) => {
                var studentEnrollments = await educationStore.GetStudentEnrollmentsForOfferingAsync(
                    new CourseOfferingId(offeringId), cancellationToken);
                return Results.Ok(studentEnrollments);
            });

        // Assessments
        app.MapPost("/education/assessments", async (HttpContext context, Assessment assessment, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
                var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
                if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role)) {
                    if (!string.Equals(assessment.InstitutionId.Value, callerAccess.InstitutionId,
                            StringComparison.OrdinalIgnoreCase)) {
                        return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }
                }

                await educationStore.CreateAssessmentAsync(assessment, cancellationToken);
                return Results.Ok(assessment);
            });

        app.MapGet("/education/assessments/{id}", async (string id, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
                var assessment = await educationStore.GetAssessmentAsync(new AssessmentId(id), cancellationToken);
                return assessment is null ? Results.NotFound($"Assessment '{id}' not found.") : Results.Ok(assessment);
            });

        // Users / students
        app.MapPost("/education/users", async (HttpContext context, User user, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
                var callerAccess = VerifierAuthorizationHelpers.TryGetAccessContext(context);
                if (callerAccess is not null && !VerifierRolePolicy.IsOperatorRole(callerAccess.Role)) {
                    if (!string.Equals(user.InstitutionId.Value, callerAccess.InstitutionId, StringComparison.OrdinalIgnoreCase)) {
                        return Results.StatusCode(StatusCodes.Status403Forbidden);
                    }
                }

                await educationStore.CreateUserAsync(user, cancellationToken);
                return Results.Ok(user);
            });

        app.MapGet("/education/users/{id}", async (string id, IEducationStore educationStore,
            CancellationToken cancellationToken) => {
                var user = await educationStore.GetUserAsync(new UserId(id), cancellationToken);
                return user is null ? Results.NotFound($"User '{id}' not found.") : Results.Ok(user);
            });
    }
}
