namespace Ows.Core.Education;

/// <summary>
/// Defines the storage interface for persisting and retrieving educational domain models.
/// </summary>
public interface IEducationStore {
    /// <summary>
    /// Creates or updates an institution record.
    /// </summary>
    Task CreateInstitutionAsync(Institution institution, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves an institution by its unique identifier.
    /// </summary>
    Task<Institution?> GetInstitutionAsync(InstitutionId id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates a course record.
    /// </summary>
    Task CreateCourseAsync(Course course, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a course by its unique identifier.
    /// </summary>
    Task<Course?> GetCourseAsync(CourseId id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates a class group record.
    /// </summary>
    Task CreateClassGroupAsync(ClassGroup classGroup, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a class group by its unique identifier.
    /// </summary>
    Task<ClassGroup?> GetClassGroupAsync(ClassGroupId id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates a user/student record.
    /// </summary>
    Task CreateUserAsync(User user, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a user by their unique identifier.
    /// </summary>
    Task<User?> GetUserAsync(UserId id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates a course offering record.
    /// </summary>
    Task CreateCourseOfferingAsync(CourseOffering offering, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a course offering by its unique identifier.
    /// </summary>
    Task<CourseOffering?> GetCourseOfferingAsync(CourseOfferingId id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates a student enrollment record.
    /// </summary>
    Task CreateStudentEnrollmentAsync(StudentEnrollment studentEnrollment, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all student enrollments registered for a specific student.
    /// </summary>
    Task<IReadOnlyList<StudentEnrollment>> GetStudentEnrollmentsForStudentAsync(
        UserId studentUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all student enrollments registered for a specific course offering.
    /// </summary>
    Task<IReadOnlyList<StudentEnrollment>> GetStudentEnrollmentsForOfferingAsync(
        CourseOfferingId offeringId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates or updates an assessment record.
    /// </summary>
    Task CreateAssessmentAsync(Assessment assessment, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves an assessment by its unique identifier.
    /// </summary>
    Task<Assessment?> GetAssessmentAsync(AssessmentId id, CancellationToken cancellationToken);

    /// <summary>
    /// Initializes the store (ensures directory structure, connection pools, or schemas are ready).
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);
}
