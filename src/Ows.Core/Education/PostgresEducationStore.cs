using Npgsql;

namespace Ows.Core.Education;

/// <summary>
/// Persists educational domain models in PostgreSQL.
/// </summary>
public sealed class PostgresEducationStore : IEducationStore
{
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresEducationStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    public PostgresEducationStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        // Database migration is executed by PostgresVerifierMigrator during server startup.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task CreateInstitutionAsync(Institution institution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(institution);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              insert into edu_institutions (id, name, slug, created_at)
                              values (@id, @name, @slug, @created_at)
                              on conflict (id) do update set name = @name, slug = @slug;
                              """;
        command.Parameters.AddWithValue("id", institution.Id.Value);
        command.Parameters.AddWithValue("name", institution.Name);
        command.Parameters.AddWithValue("slug", institution.Slug);
        command.Parameters.AddWithValue("created_at", institution.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Institution?> GetInstitutionAsync(InstitutionId id, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select id, name, slug, created_at from edu_institutions where id = @id;";
        command.Parameters.AddWithValue("id", id.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new Institution(
                new InstitutionId(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3)
            );
        }
        return null;
    }

    /// <inheritdoc />
    public async Task CreateCourseAsync(Course course, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(course);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              insert into edu_courses (id, institution_id, code, title, created_at)
                              values (@id, @institution_id, @code, @title, @created_at)
                              on conflict (id) do update set code = @code, title = @title;
                              """;
        command.Parameters.AddWithValue("id", course.Id.Value);
        command.Parameters.AddWithValue("institution_id", course.InstitutionId.Value);
        command.Parameters.AddWithValue("code", course.Code);
        command.Parameters.AddWithValue("title", course.Title);
        command.Parameters.AddWithValue("created_at", course.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Course?> GetCourseAsync(CourseId id, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select id, institution_id, code, title, created_at from edu_courses where id = @id;";
        command.Parameters.AddWithValue("id", id.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new Course(
                new CourseId(reader.GetString(0)),
                new InstitutionId(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4)
            );
        }
        return null;
    }

    /// <inheritdoc />
    public async Task CreateClassGroupAsync(ClassGroup classGroup, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(classGroup);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              insert into edu_classes (id, institution_id, name, created_at)
                              values (@id, @institution_id, @name, @created_at)
                              on conflict (id) do update set name = @name;
                              """;
        command.Parameters.AddWithValue("id", classGroup.Id.Value);
        command.Parameters.AddWithValue("institution_id", classGroup.InstitutionId.Value);
        command.Parameters.AddWithValue("name", classGroup.Name);
        command.Parameters.AddWithValue("created_at", classGroup.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ClassGroup?> GetClassGroupAsync(ClassGroupId id, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select id, institution_id, name, created_at from edu_classes where id = @id;";
        command.Parameters.AddWithValue("id", id.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new ClassGroup(
                new ClassGroupId(reader.GetString(0)),
                new InstitutionId(reader.GetString(1)),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3)
            );
        }
        return null;
    }

    /// <inheritdoc />
    public async Task CreateUserAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              insert into edu_users (id, institution_id, display_name, external_id, email, created_at)
                              values (@id, @institution_id, @display_name, @external_id, @email, @created_at)
                              on conflict (id) do update set display_name = @display_name, external_id = @external_id, email = @email;
                              """;
        command.Parameters.AddWithValue("id", user.Id.Value);
        command.Parameters.AddWithValue("institution_id", user.InstitutionId.Value);
        command.Parameters.AddWithValue("display_name", user.DisplayName);
        command.Parameters.AddWithValue("external_id", (object?)user.ExternalId ?? DBNull.Value);
        command.Parameters.AddWithValue("email", (object?)user.Email ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", user.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<User?> GetUserAsync(UserId id, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select id, institution_id, display_name, external_id, email, created_at from edu_users where id = @id;";
        command.Parameters.AddWithValue("id", id.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new User(
                new UserId(reader.GetString(0)),
                new InstitutionId(reader.GetString(1)),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5)
            );
        }
        return null;
    }

    /// <inheritdoc />
    public async Task CreateCourseOfferingAsync(CourseOffering offering, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offering);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              insert into edu_course_offerings (id, institution_id, course_id, class_group_id, term, year, created_at)
                              values (@id, @institution_id, @course_id, @class_group_id, @term, @year, @created_at)
                              on conflict (id) do update set term = @term, year = @year;
                              """;
        command.Parameters.AddWithValue("id", offering.Id.Value);
        command.Parameters.AddWithValue("institution_id", offering.InstitutionId.Value);
        command.Parameters.AddWithValue("course_id", offering.CourseId.Value);
        command.Parameters.AddWithValue("class_group_id", offering.ClassGroupId.Value);
        command.Parameters.AddWithValue("term", offering.Term);
        command.Parameters.AddWithValue("year", offering.Year);
        command.Parameters.AddWithValue("created_at", offering.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CourseOffering?> GetCourseOfferingAsync(CourseOfferingId id, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select id, institution_id, course_id, class_group_id, term, year, created_at from edu_course_offerings where id = @id;";
        command.Parameters.AddWithValue("id", id.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new CourseOffering(
                new CourseOfferingId(reader.GetString(0)),
                new InstitutionId(reader.GetString(1)),
                new CourseId(reader.GetString(2)),
                new ClassGroupId(reader.GetString(3)),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetFieldValue<DateTimeOffset>(6)
            );
        }
        return null;
    }

    /// <inheritdoc />
    public async Task CreateEnrollmentAsync(Enrollment enrollment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              insert into edu_enrollments (id, course_offering_id, user_id, role, created_at)
                              values (@id, @course_offering_id, @user_id, @role, @created_at)
                              on conflict (id) do update set role = @role;
                              """;
        command.Parameters.AddWithValue("id", enrollment.Id.Value);
        command.Parameters.AddWithValue("course_offering_id", enrollment.CourseOfferingId.Value);
        command.Parameters.AddWithValue("user_id", enrollment.UserId.Value);
        command.Parameters.AddWithValue("role", enrollment.Role.ToString());
        command.Parameters.AddWithValue("created_at", enrollment.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Enrollment>> GetEnrollmentsForUserAsync(UserId userId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select id, course_offering_id, user_id, role, created_at from edu_enrollments where user_id = @user_id;";
        command.Parameters.AddWithValue("user_id", userId.Value);
        var list = new List<Enrollment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Enum.TryParse<EducationRole>(reader.GetString(3), out var role);
            list.Add(new Enrollment(
                new EnrollmentId(reader.GetString(0)),
                new CourseOfferingId(reader.GetString(1)),
                new UserId(reader.GetString(2)),
                role,
                reader.GetFieldValue<DateTimeOffset>(4)
            ));
        }
        return list;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Enrollment>> GetEnrollmentsForOfferingAsync(CourseOfferingId offeringId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select id, course_offering_id, user_id, role, created_at from edu_enrollments where course_offering_id = @offering_id;";
        command.Parameters.AddWithValue("offering_id", offeringId.Value);
        var list = new List<Enrollment>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            Enum.TryParse<EducationRole>(reader.GetString(3), out var role);
            list.Add(new Enrollment(
                new EnrollmentId(reader.GetString(0)),
                new CourseOfferingId(reader.GetString(1)),
                new UserId(reader.GetString(2)),
                role,
                reader.GetFieldValue<DateTimeOffset>(4)
            ));
        }
        return list;
    }

    /// <inheritdoc />
    public async Task CreateAssessmentAsync(Assessment assessment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              insert into edu_assessments (id, institution_id, course_offering_id, title, starts_at, ends_at, policy_id, created_at)
                              values (@id, @institution_id, @course_offering_id, @title, @starts_at, @ends_at, @policy_id, @created_at)
                              on conflict (id) do update set title = @title, starts_at = @starts_at, ends_at = @ends_at, policy_id = @policy_id;
                              """;
        command.Parameters.AddWithValue("id", assessment.Id.Value);
        command.Parameters.AddWithValue("institution_id", assessment.InstitutionId.Value);
        command.Parameters.AddWithValue("course_offering_id", assessment.CourseOfferingId.Value);
        command.Parameters.AddWithValue("title", assessment.Title);
        command.Parameters.AddWithValue("starts_at", (object?)assessment.StartsAt ?? DBNull.Value);
        command.Parameters.AddWithValue("ends_at", (object?)assessment.EndsAt ?? DBNull.Value);
        command.Parameters.AddWithValue("policy_id", (object?)assessment.PolicyId?.Value ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", assessment.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Assessment?> GetAssessmentAsync(AssessmentId id, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "select id, institution_id, course_offering_id, title, starts_at, ends_at, policy_id, created_at from edu_assessments where id = @id;";
        command.Parameters.AddWithValue("id", id.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new Assessment(
                new AssessmentId(reader.GetString(0)),
                new InstitutionId(reader.GetString(1)),
                new CourseOfferingId(reader.GetString(2)),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : new PolicyId(reader.GetString(6)),
                reader.GetFieldValue<DateTimeOffset>(7)
            );
        }
        return null;
    }
}
