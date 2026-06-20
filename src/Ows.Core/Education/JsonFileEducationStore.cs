using System.Text.Json;

namespace Ows.Core.Education;

/// <summary>
/// Persists educational domain models to a local JSON file.
/// </summary>
public sealed class JsonFileEducationStore : IEducationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly Lock _gate = new();
    private readonly string _storePath;

    private Dictionary<string, Institution> _institutions = [];
    private Dictionary<string, Course> _courses = [];
    private Dictionary<string, ClassGroup> _classes = [];
    private Dictionary<string, User> _users = [];
    private Dictionary<string, CourseOffering> _offerings = [];
    private Dictionary<string, StudentEnrollment> _studentEnrollments = [];
    private Dictionary<string, Assessment> _assessments = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonFileEducationStore"/> class.
    /// </summary>
    /// <param name="storePath">The path to the JSON storage file.</param>
    public JsonFileEducationStore(string storePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        _storePath = storePath;
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        LoadFromDisk();
        return Task.CompletedTask;
    }

    private void LoadFromDisk()
    {
        lock (_gate)
        {
            if (!File.Exists(_storePath)) return;
            try
            {
                var text = File.ReadAllText(_storePath);
                var data = JsonSerializer.Deserialize<EducationJsonData>(text, SerializerOptions);
                if (data is not null)
                {
                    _institutions = data.Institutions ?? [];
                    _courses = data.Courses ?? [];
                    _classes = data.Classes ?? [];
                    _users = data.Users ?? [];
                    _offerings = data.Offerings ?? [];
                    _studentEnrollments = data.StudentEnrollments ?? data.Enrollments ?? [];
                    _assessments = data.Assessments ?? [];
                }
            }
            catch
            {
                // Ignore load failures (start empty)
            }
        }
    }

    private void SaveToDisk()
    {
        lock (_gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(_storePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var data = new EducationJsonData
                {
                    Institutions = _institutions,
                    Courses = _courses,
                    Classes = _classes,
                    Users = _users,
                    Offerings = _offerings,
                    StudentEnrollments = _studentEnrollments,
                    Assessments = _assessments
                };

                var tempPath = _storePath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(data, SerializerOptions));
                File.Move(tempPath, _storePath, overwrite: true);
            }
            catch
            {
                // Ignore write failures
            }
        }
    }

    /// <inheritdoc />
    public Task CreateInstitutionAsync(Institution institution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institution);
        lock (_gate)
        {
            _institutions[institution.Id.Value] = institution;
            SaveToDisk();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Institution?> GetInstitutionAsync(InstitutionId id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_institutions.GetValueOrDefault(id.Value));
        }
    }

    /// <inheritdoc />
    public Task CreateCourseAsync(Course course, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(course);
        lock (_gate)
        {
            _courses[course.Id.Value] = course;
            SaveToDisk();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Course?> GetCourseAsync(CourseId id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_courses.GetValueOrDefault(id.Value));
        }
    }

    /// <inheritdoc />
    public Task CreateClassGroupAsync(ClassGroup classGroup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(classGroup);
        lock (_gate)
        {
            _classes[classGroup.Id.Value] = classGroup;
            SaveToDisk();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ClassGroup?> GetClassGroupAsync(ClassGroupId id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_classes.GetValueOrDefault(id.Value));
        }
    }

    /// <inheritdoc />
    public Task CreateUserAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        lock (_gate)
        {
            _users[user.Id.Value] = user;
            SaveToDisk();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<User?> GetUserAsync(UserId id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_users.GetValueOrDefault(id.Value));
        }
    }

    /// <inheritdoc />
    public Task CreateCourseOfferingAsync(CourseOffering offering, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offering);
        lock (_gate)
        {
            _offerings[offering.Id.Value] = offering;
            SaveToDisk();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CourseOffering?> GetCourseOfferingAsync(CourseOfferingId id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_offerings.GetValueOrDefault(id.Value));
        }
    }

    /// <inheritdoc />
    public Task CreateStudentEnrollmentAsync(StudentEnrollment studentEnrollment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(studentEnrollment);
        lock (_gate)
        {
            _studentEnrollments[studentEnrollment.Id.Value] = studentEnrollment;
            SaveToDisk();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StudentEnrollment>> GetStudentEnrollmentsForStudentAsync(
        UserId studentUserId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<StudentEnrollment> result = _studentEnrollments.Values
                .Where(e => e.StudentUserId == studentUserId)
                .ToList();
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StudentEnrollment>> GetStudentEnrollmentsForOfferingAsync(
        CourseOfferingId offeringId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<StudentEnrollment> result = _studentEnrollments.Values
                .Where(e => e.CourseOfferingId == offeringId)
                .ToList();
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task CreateAssessmentAsync(Assessment assessment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        lock (_gate)
        {
            _assessments[assessment.Id.Value] = assessment;
            SaveToDisk();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Assessment?> GetAssessmentAsync(AssessmentId id, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_assessments.GetValueOrDefault(id.Value));
        }
    }

    private sealed class EducationJsonData
    {
        public Dictionary<string, Institution>? Institutions { get; init; }
        public Dictionary<string, Course>? Courses { get; init; }
        public Dictionary<string, ClassGroup>? Classes { get; init; }
        public Dictionary<string, User>? Users { get; init; }
        public Dictionary<string, CourseOffering>? Offerings { get; init; }
        public Dictionary<string, StudentEnrollment>? StudentEnrollments { get; init; }
        public Dictionary<string, StudentEnrollment>? Enrollments { get; init; }
        public Dictionary<string, Assessment>? Assessments { get; init; }
    }
}
