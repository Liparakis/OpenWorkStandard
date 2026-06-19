namespace Ows.Core.Education;

/// <summary>
/// Defines educational participant roles.
/// </summary>
public enum EducationRole
{
    /// <summary>
    /// Student pursuing course assessments.
    /// </summary>
    Student,

    /// <summary>
    /// Instructor verifying course assessments.
    /// </summary>
    Instructor,

    /// <summary>
    /// Admin managing a course offering and assessments.
    /// </summary>
    CourseAdmin,

    /// <summary>
    /// Administrator managing an entire institution.
    /// </summary>
    InstitutionAdmin
}
