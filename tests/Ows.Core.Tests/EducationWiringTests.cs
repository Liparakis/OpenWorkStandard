using FluentAssertions;
using Ows.Core.Education;
using Ows.Core.Verification;

namespace Ows.Core.Tests;

/// <summary>
/// Tests the education wiring layer: JsonFileEducationStore round-trips and
/// the validation rules that guard session creation with education context.
/// </summary>
public sealed class EducationWiringTests : IDisposable
{
    private readonly string _storeDirectory;
    private readonly string _storePath;
    private readonly JsonFileEducationStore _store;

    /// <summary>
    /// Initializes a temporary education store for each test.
    /// </summary>
    public EducationWiringTests()
    {
        _storeDirectory = Path.Combine(Path.GetTempPath(), $"ows-edu-{Guid.NewGuid():N}");
        _storePath = Path.Combine(_storeDirectory, "education.json");
        _store = new JsonFileEducationStore(_storePath);
        _store.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_storeDirectory))
        {
            Directory.Delete(_storeDirectory, recursive: true);
        }
    }

    // ── Institution round-trip ────────────────────────────────────────────

    /// <summary>
    /// Verifies that a created institution can be retrieved by ID.
    /// </summary>
    [Fact]
    public async Task CreateAndGetInstitutionAsync_ShouldRoundTrip()
    {
        var id = InstitutionId.Create();
        var institution = new Institution(id, "Open University", "open-university", DateTimeOffset.UtcNow);

        await _store.CreateInstitutionAsync(institution, CancellationToken.None);
        var retrieved = await _store.GetInstitutionAsync(id, CancellationToken.None);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(id);
        retrieved.Name.Should().Be("Open University");
        retrieved.Slug.Should().Be("open-university");
    }

    /// <summary>
    /// Verifies that a missing institution returns null.
    /// </summary>
    [Fact]
    public async Task GetInstitutionAsync_WhenNotFound_ShouldReturnNull()
    {
        var result = await _store.GetInstitutionAsync(new InstitutionId("does-not-exist"), CancellationToken.None);
        result.Should().BeNull();
    }

    // ── Course round-trip ─────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a created course can be retrieved by ID.
    /// </summary>
    [Fact]
    public async Task CreateAndGetCourseAsync_ShouldRoundTrip()
    {
        var institutionId = InstitutionId.Create();
        var courseId = CourseId.Create();
        var course = new Course(courseId, institutionId, "CS101", "Intro to CS", DateTimeOffset.UtcNow);

        await _store.CreateCourseAsync(course, CancellationToken.None);
        var retrieved = await _store.GetCourseAsync(courseId, CancellationToken.None);

        retrieved.Should().NotBeNull();
        retrieved!.Code.Should().Be("CS101");
        retrieved.Title.Should().Be("Intro to CS");
    }

    // ── User / student round-trip ─────────────────────────────────────────

    /// <summary>
    /// Verifies that a created user can be retrieved by ID.
    /// </summary>
    [Fact]
    public async Task CreateAndGetUserAsync_ShouldRoundTrip()
    {
        var institutionId = InstitutionId.Create();
        var userId = UserId.Create();
        var user = new User(userId, institutionId, "Jane Smith", "S12345", "jane@university.edu", DateTimeOffset.UtcNow);

        await _store.CreateUserAsync(user, CancellationToken.None);
        var retrieved = await _store.GetUserAsync(userId, CancellationToken.None);

        retrieved.Should().NotBeNull();
        retrieved!.DisplayName.Should().Be("Jane Smith");
        retrieved.ExternalId.Should().Be("S12345");
        retrieved.Email.Should().Be("jane@university.edu");
    }

    // ── Assessment round-trip ─────────────────────────────────────────────

    /// <summary>
    /// Verifies that a created assessment can be retrieved by ID.
    /// </summary>
    [Fact]
    public async Task CreateAndGetAssessmentAsync_ShouldRoundTrip()
    {
        var institutionId = InstitutionId.Create();
        var offeringId = CourseOfferingId.Create();
        var assessmentId = AssessmentId.Create();
        var assessment = new Assessment(
            assessmentId,
            institutionId,
            offeringId,
            "Midterm Exam",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(2),
            null,
            DateTimeOffset.UtcNow);

        await _store.CreateAssessmentAsync(assessment, CancellationToken.None);
        var retrieved = await _store.GetAssessmentAsync(assessmentId, CancellationToken.None);

        retrieved.Should().NotBeNull();
        retrieved!.Title.Should().Be("Midterm Exam");
        retrieved.InstitutionId.Should().Be(institutionId);
        retrieved.CourseOfferingId.Should().Be(offeringId);
    }

    // ── Session start validation logic ────────────────────────────────────

    /// <summary>
    /// Verifies that session validation accepts a valid institution + assessment + student.
    /// </summary>
    [Fact]
    public async Task ValidateSessionEducationContext_WhenAllValid_ShouldSucceed()
    {
        var institutionId = InstitutionId.Create();
        var offeringId = CourseOfferingId.Create();
        var assessmentId = AssessmentId.Create();
        var userId = UserId.Create();

        await _store.CreateInstitutionAsync(
            new Institution(institutionId, "Test University", "test-u", DateTimeOffset.UtcNow),
            CancellationToken.None);
        await _store.CreateAssessmentAsync(
            new Assessment(assessmentId, institutionId, offeringId, "Final", null, null, null, DateTimeOffset.UtcNow),
            CancellationToken.None);
        await _store.CreateUserAsync(
            new User(userId, institutionId, "Alice", null, null, DateTimeOffset.UtcNow),
            CancellationToken.None);

        var (error, _) = await ValidateEducationContextAsync(
            institutionId.Value, assessmentId.Value, userId.Value);

        error.Should().BeNull("all entities exist and belong to the same institution");
    }

    /// <summary>
    /// Verifies that session validation rejects an unknown institution.
    /// </summary>
    [Fact]
    public async Task ValidateSessionEducationContext_WhenInstitutionMissing_ShouldReturnError()
    {
        var (error, _) = await ValidateEducationContextAsync("nonexistent-inst", null, null);
        error.Should().Contain("not found");
    }

    /// <summary>
    /// Verifies that session validation rejects an assessment that does not belong to the institution.
    /// </summary>
    [Fact]
    public async Task ValidateSessionEducationContext_WhenAssessmentBelongsToDifferentInstitution_ShouldReturnError()
    {
        var instA = InstitutionId.Create();
        var instB = InstitutionId.Create();
        var offeringId = CourseOfferingId.Create();
        var assessmentId = AssessmentId.Create();

        await _store.CreateInstitutionAsync(
            new Institution(instA, "Inst A", "inst-a", DateTimeOffset.UtcNow), CancellationToken.None);
        await _store.CreateInstitutionAsync(
            new Institution(instB, "Inst B", "inst-b", DateTimeOffset.UtcNow), CancellationToken.None);
        // Assessment belongs to instB, but session claims instA
        await _store.CreateAssessmentAsync(
            new Assessment(assessmentId, instB, offeringId, "Exam", null, null, null, DateTimeOffset.UtcNow),
            CancellationToken.None);

        var (error, _) = await ValidateEducationContextAsync(instA.Value, assessmentId.Value, null);
        error.Should().Contain("does not belong to institution");
    }

    /// <summary>
    /// Verifies that session validation rejects an unknown student user.
    /// </summary>
    [Fact]
    public async Task ValidateSessionEducationContext_WhenStudentMissing_ShouldReturnError()
    {
        var institutionId = InstitutionId.Create();
        await _store.CreateInstitutionAsync(
            new Institution(institutionId, "Test U", "test-u2", DateTimeOffset.UtcNow), CancellationToken.None);

        var (error, _) = await ValidateEducationContextAsync(institutionId.Value, null, "ghost-user");
        error.Should().Contain("not found");
    }

    // ── ReportEducationContext assembly ───────────────────────────────────

    /// <summary>
    /// Verifies that ReportEducationContext is fully populated from store lookups.
    /// </summary>
    [Fact]
    public async Task ResolveEducationContext_WhenAllEntitiesPresent_ShouldPopulateAllFields()
    {
        var institutionId = InstitutionId.Create();
        var courseId = CourseId.Create();
        var classGroupId = ClassGroupId.Create();
        var offeringId = CourseOfferingId.Create();
        var assessmentId = AssessmentId.Create();
        var userId = UserId.Create();

        var institution = new Institution(institutionId, "Grand University", "grand-u", DateTimeOffset.UtcNow);
        var course = new Course(courseId, institutionId, "ENG200", "Literary Analysis", DateTimeOffset.UtcNow);
        var classGroup = new ClassGroup(classGroupId, institutionId, "Section A", DateTimeOffset.UtcNow);
        var offering = new CourseOffering(offeringId, institutionId, courseId, classGroupId, "Fall", 2025, DateTimeOffset.UtcNow);
        var assessment = new Assessment(assessmentId, institutionId, offeringId, "Essay 1", null, null, null, DateTimeOffset.UtcNow);
        var user = new User(userId, institutionId, "Bob Jones", "B99", "bob@grand.edu", DateTimeOffset.UtcNow);

        await _store.CreateInstitutionAsync(institution, CancellationToken.None);
        await _store.CreateCourseAsync(course, CancellationToken.None);
        await _store.CreateClassGroupAsync(classGroup, CancellationToken.None);
        await _store.CreateCourseOfferingAsync(offering, CancellationToken.None);
        await _store.CreateAssessmentAsync(assessment, CancellationToken.None);
        await _store.CreateUserAsync(user, CancellationToken.None);

        var context = await ResolveContextAsync(institutionId.Value, assessmentId.Value, userId.Value);

        context.Should().NotBeNull();
        context!.InstitutionId.Should().Be(institutionId.Value);
        context.InstitutionName.Should().Be("Grand University");
        context.CourseId.Should().Be(courseId.Value);
        context.CourseCode.Should().Be("ENG200");
        context.CourseTitle.Should().Be("Literary Analysis");
        context.AssessmentId.Should().Be(assessmentId.Value);
        context.AssessmentTitle.Should().Be("Essay 1");
        context.StudentUserId.Should().Be(userId.Value);
        context.StudentDisplayName.Should().Be("Bob Jones");
        context.StudentExternalId.Should().Be("B99");
    }

    /// <summary>
    /// Verifies that null is returned when no education identifiers are supplied.
    /// </summary>
    [Fact]
    public async Task ResolveEducationContext_WhenNoIdentifiers_ShouldReturnNull()
    {
        var context = await ResolveContextAsync(null, null, null);
        context.Should().BeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // Mirrors the validation logic in Program.cs POST /sessions
    private async Task<(string? error, string? metadata)> ValidateEducationContextAsync(
        string? institutionId,
        string? assessmentId,
        string? studentUserId)
    {
        if (string.IsNullOrWhiteSpace(institutionId)
            && string.IsNullOrWhiteSpace(assessmentId)
            && string.IsNullOrWhiteSpace(studentUserId))
        {
            return (null, null);
        }

        if (string.IsNullOrWhiteSpace(institutionId))
        {
            return ("InstitutionId is required when education context is supplied.", null);
        }

        var institution = await _store.GetInstitutionAsync(new InstitutionId(institutionId), CancellationToken.None);
        if (institution is null)
        {
            return ($"Institution '{institutionId}' not found.", null);
        }

        if (!string.IsNullOrWhiteSpace(assessmentId))
        {
            var assessment = await _store.GetAssessmentAsync(new AssessmentId(assessmentId), CancellationToken.None);
            if (assessment is null)
            {
                return ($"Assessment '{assessmentId}' not found.", null);
            }
            if (!string.Equals(assessment.InstitutionId.Value, institutionId, StringComparison.OrdinalIgnoreCase))
            {
                return ($"Assessment '{assessmentId}' does not belong to institution '{institutionId}'.", null);
            }
        }

        if (!string.IsNullOrWhiteSpace(studentUserId))
        {
            var student = await _store.GetUserAsync(new UserId(studentUserId), CancellationToken.None);
            if (student is null)
            {
                return ($"Student user '{studentUserId}' not found.", null);
            }
        }

        return (null, "{}");
    }

    // Mirrors ResolveEducationContextAsync from Program.cs
    private async Task<ReportEducationContext?> ResolveContextAsync(
        string? institutionId,
        string? assessmentId,
        string? studentUserId)
    {
        if (string.IsNullOrWhiteSpace(institutionId)
            && string.IsNullOrWhiteSpace(assessmentId)
            && string.IsNullOrWhiteSpace(studentUserId))
        {
            return null;
        }

        Education.Institution? institution = null;
        if (!string.IsNullOrWhiteSpace(institutionId))
        {
            institution = await _store.GetInstitutionAsync(new InstitutionId(institutionId), CancellationToken.None);
        }

        Education.Assessment? assessment = null;
        Education.Course? course = null;
        Education.CourseOffering? offering = null;
        if (!string.IsNullOrWhiteSpace(assessmentId))
        {
            assessment = await _store.GetAssessmentAsync(new AssessmentId(assessmentId), CancellationToken.None);
            if (assessment is not null)
            {
                offering = await _store.GetCourseOfferingAsync(assessment.CourseOfferingId, CancellationToken.None);
                if (offering is not null)
                {
                    course = await _store.GetCourseAsync(offering.CourseId, CancellationToken.None);
                }
            }
        }

        Education.User? student = null;
        if (!string.IsNullOrWhiteSpace(studentUserId))
        {
            student = await _store.GetUserAsync(new UserId(studentUserId), CancellationToken.None);
        }

        return new ReportEducationContext
        {
            InstitutionId = institution?.Id.Value,
            InstitutionName = institution?.Name,
            CourseId = course?.Id.Value,
            CourseCode = course?.Code,
            CourseTitle = course?.Title,
            AssessmentId = assessment?.Id.Value,
            AssessmentTitle = assessment?.Title,
            StudentUserId = student?.Id.Value,
            StudentDisplayName = student?.DisplayName,
            StudentExternalId = student?.ExternalId
        };
    }
}
