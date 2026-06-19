using System;

namespace Ows.Core.Education;

/// <summary>
/// Identifies an institution in OWS.
/// </summary>
/// <param name="Value">The stable institution identifier value.</param>
public readonly record struct InstitutionId(string Value)
{
    /// <summary>
    /// Creates a new institution identifier.
    /// </summary>
    public static InstitutionId Create() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Returns the underlying identifier value.
    /// </summary>
    public override string ToString() => Value;
}

/// <summary>
/// Identifies a course in OWS.
/// </summary>
/// <param name="Value">The stable course identifier value.</param>
public readonly record struct CourseId(string Value)
{
    /// <summary>
    /// Creates a new course identifier.
    /// </summary>
    public static CourseId Create() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Returns the underlying identifier value.
    /// </summary>
    public override string ToString() => Value;
}

/// <summary>
/// Identifies a class group in OWS.
/// </summary>
/// <param name="Value">The stable class group identifier value.</param>
public readonly record struct ClassGroupId(string Value)
{
    /// <summary>
    /// Creates a new class group identifier.
    /// </summary>
    public static ClassGroupId Create() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Returns the underlying identifier value.
    /// </summary>
    public override string ToString() => Value;
}

/// <summary>
/// Identifies a course offering in OWS.
/// </summary>
/// <param name="Value">The stable course offering identifier value.</param>
public readonly record struct CourseOfferingId(string Value)
{
    /// <summary>
    /// Creates a new course offering identifier.
    /// </summary>
    public static CourseOfferingId Create() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Returns the underlying identifier value.
    /// </summary>
    public override string ToString() => Value;
}

/// <summary>
/// Identifies a user in OWS.
/// </summary>
/// <param name="Value">The stable user identifier value.</param>
public readonly record struct UserId(string Value)
{
    /// <summary>
    /// Creates a new user identifier.
    /// </summary>
    public static UserId Create() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Returns the underlying identifier value.
    /// </summary>
    public override string ToString() => Value;
}

/// <summary>
/// Identifies an enrollment in OWS.
/// </summary>
/// <param name="Value">The stable enrollment identifier value.</param>
public readonly record struct EnrollmentId(string Value)
{
    /// <summary>
    /// Creates a new enrollment identifier.
    /// </summary>
    public static EnrollmentId Create() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Returns the underlying identifier value.
    /// </summary>
    public override string ToString() => Value;
}

/// <summary>
/// Identifies an assessment in OWS.
/// </summary>
/// <param name="Value">The stable assessment identifier value.</param>
public readonly record struct AssessmentId(string Value)
{
    /// <summary>
    /// Creates a new assessment identifier.
    /// </summary>
    public static AssessmentId Create() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Returns the underlying identifier value.
    /// </summary>
    public override string ToString() => Value;
}

/// <summary>
/// Identifies an assessment policy in OWS.
/// </summary>
/// <param name="Value">The stable policy identifier value.</param>
public readonly record struct PolicyId(string Value)
{
    /// <summary>
    /// Creates a new policy identifier.
    /// </summary>
    public static PolicyId Create() => new(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Returns the underlying identifier value.
    /// </summary>
    public override string ToString() => Value;
}
