using System.Text.Json;
using Npgsql;

namespace Ows.Verifier.Server;

/// <summary>
/// Represents one durable package verification job.
/// </summary>
// ReSharper disable UnusedAutoPropertyAccessor.Global
internal sealed record PackageVerificationJobRecord {
    /// <summary>
    /// Gets the unique identifier for the job.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the package identifier associated with the job.
    /// </summary>
    public string PackageId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the current status of the verification job (e.g. Pending, Running, Succeeded, Failed).
    /// </summary>
    public string Status { get; init; } = "Pending";

    /// <summary>
    /// Gets the number of verification attempts made for this job.
    /// </summary>
    public int Attempts { get; init; }

    /// <summary>
    /// Gets the API key ID that requested the package verification, if applicable.
    /// </summary>
    public string? RequestedByApiKeyId { get; init; }

    /// <summary>
    /// Gets the creation timestamp of the job in UTC.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Gets the start timestamp of the job in UTC, if applicable.
    /// </summary>
    public DateTimeOffset? StartedAtUtc { get; init; }

    /// <summary>
    /// Gets the completion timestamp of the job in UTC, if applicable.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; init; }

    /// <summary>
    /// Gets the error message of the last failed attempt, if applicable.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Gets the serialized JSON representation of the verification result, if applicable.
    /// </summary>
    public string? ResultJson { get; init; }
}

/// <summary>
/// Holds aggregate job counters for diagnostics.
/// </summary>
internal sealed record PackageVerificationJobSummary {
    /// <summary>
    /// Gets the number of pending jobs.
    /// </summary>
    public int Pending { get; init; }

    /// <summary>
    /// Gets the number of running jobs.
    /// </summary>
    public int Running { get; init; }

    /// <summary>
    /// Gets the number of successfully completed jobs.
    /// </summary>
    public int Succeeded { get; init; }

    /// <summary>
    /// Gets the number of failed jobs.
    /// </summary>
    public int Failed { get; init; }
}

/// <summary>
/// A JSON file-backed implementation of <see cref="IPackageVerificationJobStore"/>.
/// </summary>
internal sealed class JsonFilePackageVerificationJobStore : IPackageVerificationJobStore {
    /// <summary>
    /// The options used for JSON serialization.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>
    /// A lock object to synchronize access to the in-memory dictionary.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The in-memory collection of verification jobs, keyed by their unique job ID.
    /// </summary>
    private readonly Dictionary<string, PackageVerificationJobRecord> _jobs = [];

    /// <summary>
    /// The file path where the job definitions are stored.
    /// </summary>
    private readonly string _storePath;

    /// <summary>
    /// Initializes a new JSON job store.
    /// </summary>
    /// <param name="storePath">The path to the JSON storage file.</param>
    public JsonFilePackageVerificationJobStore(string storePath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        _storePath = storePath;
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        LoadFromDisk();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PackageVerificationJobRecord> QueueAsync(
        string packageId,
        string? requestedByApiKeyId,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate) {
            var existing = _jobs.Values
                .Where(job => string.Equals(job.PackageId, packageId, StringComparison.Ordinal))
                .OrderByDescending(job => job.CreatedAtUtc)
                .FirstOrDefault();
            if (existing is not null && (existing.Status == "Pending" || existing.Status == "Running")) {
                return Task.FromResult(existing);
            }

            var job = new PackageVerificationJobRecord {
                Id = Guid.NewGuid().ToString("N"),
                PackageId = packageId,
                Status = "Pending",
                RequestedByApiKeyId = requestedByApiKeyId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            _jobs[job.Id] = job;
            SaveToDisk();
            return Task.FromResult(job);
        }
    }

    /// <inheritdoc />
    public Task<PackageVerificationJobRecord?> TryStartNextAsync(
        TimeSpan staleRunningThreshold,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) {
            var now = DateTimeOffset.UtcNow;
            ResetStaleRunningJobs(now, staleRunningThreshold);

            var next = _jobs.Values
                .Where(job => job.Status == "Pending")
                .OrderBy(job => job.CreatedAtUtc)
                .FirstOrDefault();
            if (next is null) {
                return Task.FromResult<PackageVerificationJobRecord?>(null);
            }

            var started = next with {
                Status = "Running",
                Attempts = next.Attempts + 1,
                StartedAtUtc = now,
                CompletedAtUtc = null,
                LastError = null
            };
            _jobs[started.Id] = started;
            SaveToDisk();
            return Task.FromResult<PackageVerificationJobRecord?>(started);
        }
    }

    /// <inheritdoc />
    public Task<PackageVerificationJobRecord?> GetLatestForPackageAsync(string packageId,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) {
            var job = _jobs.Values
                .Where(candidate => string.Equals(candidate.PackageId, packageId, StringComparison.Ordinal))
                .OrderByDescending(candidate => candidate.CreatedAtUtc)
                .FirstOrDefault();
            return Task.FromResult(job);
        }
    }

    /// <inheritdoc />
    public Task CompleteAsync(
        string jobId,
        string status,
        string? resultJson,
        string? lastError,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) {
            if (!_jobs.TryGetValue(jobId, out var existing)) {
                throw new InvalidOperationException($"Verification job not found: {jobId}");
            }

            _jobs[jobId] = existing with {
                Status = status,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                ResultJson = resultJson,
                LastError = lastError
            };
            SaveToDisk();
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<PackageVerificationJobSummary> GetSummaryAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) {
            return Task.FromResult(BuildSummary(_jobs.Values));
        }
    }

    /// <summary>
    /// Loads verification jobs from the JSON file on disk.
    /// </summary>
    private void LoadFromDisk() {
        lock (_gate) {
            _jobs.Clear();
            if (!File.Exists(_storePath)) {
                return;
            }

            try {
                var json = File.ReadAllText(_storePath);
                var snapshot = JsonSerializer.Deserialize<List<PackageVerificationJobRecord>>(json, SerializerOptions);
                if (snapshot is null) {
                    return;
                }

                foreach (var job in snapshot) {
                    _jobs[job.Id] = job;
                }
            } catch {
                // ponytail: malformed local job history starts empty; add repair tooling only if operators ask for it.
            }
        }
    }

    /// <summary>
    /// Saves the current list of verification jobs to the JSON file on disk.
    /// </summary>
    private void SaveToDisk() {
        var directory = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _storePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_jobs.Values.ToList(), SerializerOptions));
        File.Move(tempPath, _storePath, overwrite: true);
    }

    /// <summary>
    /// Resets verification jobs back to Pending status if they have been running longer than the stale threshold.
    /// </summary>
    /// <param name="now">The current date time offset.</param>
    /// <param name="staleRunningThreshold">The threshold duration beyond which a running job is considered stale.</param>
    private void ResetStaleRunningJobs(DateTimeOffset now, TimeSpan staleRunningThreshold) {
        foreach (var job in _jobs.Values.Where(job =>
                     job is { Status: "Running", StartedAtUtc: not null } &&
                     now - job.StartedAtUtc.Value >= staleRunningThreshold).ToArray()) {
            _jobs[job.Id] = job with {
                Status = "Pending",
                StartedAtUtc = null,
                LastError = "Reset to pending after stale running job timeout."
            };
        }
    }

    /// <summary>
    /// Aggregates list of jobs into a status summary.
    /// </summary>
    /// <param name="jobs">The list of jobs to aggregate.</param>
    /// <returns>A summary statistics record.</returns>
    private static PackageVerificationJobSummary BuildSummary(IEnumerable<PackageVerificationJobRecord> jobs) {
        var counts = jobs.GroupBy(static job => job.Status, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);
        return new PackageVerificationJobSummary {
            Pending = counts.GetValueOrDefault("Pending"),
            Running = counts.GetValueOrDefault("Running"),
            Succeeded = counts.GetValueOrDefault("Succeeded"),
            Failed = counts.GetValueOrDefault("Failed")
        };
    }
}

/// <summary>
/// A PostgreSQL-backed implementation of <see cref="IPackageVerificationJobStore"/>.
/// </summary>
internal sealed class PostgresPackageVerificationJobStore : IPackageVerificationJobStore, IAsyncDisposable {
    /// <summary>
    /// The database connection data source.
    /// </summary>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// The background initialization task running migrations.
    /// </summary>
    private readonly Task _initializationTask;

    /// <summary>
    /// Initializes a PostgreSQL-backed job store.
    /// </summary>
    /// <param name="connectionString">The DB connection string.</param>
    /// <param name="applyMigrationsOnStartup">Whether schema migration should run automatically on startup.</param>
    public PostgresPackageVerificationJobStore(string connectionString, bool applyMigrationsOnStartup = true) {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _initializationTask = applyMigrationsOnStartup
            ? Core.Notarization.PostgresVerifierMigrator.MigrateAsync(_dataSource)
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken) {
        await _initializationTask.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PackageVerificationJobRecord> QueueAsync(
        string packageId,
        string? requestedByApiKeyId,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await using (var existingCommand = connection.CreateCommand()) {
            existingCommand.CommandText = """
                                          select id, package_id, status, attempts, requested_by_api_key_id, created_at, started_at, completed_at, last_error, result_json
                                          from ows_package_verification_jobs
                                          where package_id = @package_id
                                          order by created_at desc, id desc
                                          limit 1;
                                          """;
            existingCommand.Parameters.AddWithValue("package_id", packageId);
            await using var existingReader = await existingCommand.ExecuteReaderAsync(cancellationToken);
            if (await existingReader.ReadAsync(cancellationToken)) {
                var existing = ReadJob(existingReader);
                if (existing.Status is "Pending" or "Running") {
                    return existing;
                }
            }
        }

        var job = new PackageVerificationJobRecord {
            Id = Guid.NewGuid().ToString("N"),
            PackageId = packageId,
            Status = "Pending",
            RequestedByApiKeyId = requestedByApiKeyId,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              insert into ows_package_verification_jobs (
                                  id,
                                  package_id,
                                  status,
                                  attempts,
                                  requested_by_api_key_id,
                                  created_at,
                                  started_at,
                                  completed_at,
                                  last_error,
                                  result_json
                              )
                              values (
                                  @id,
                                  @package_id,
                                  @status,
                                  @attempts,
                                  @requested_by_api_key_id,
                                  @created_at,
                                  @started_at,
                                  @completed_at,
                                  @last_error,
                                  @result_json
                              );
                              """;
        command.Parameters.AddWithValue("id", job.Id);
        command.Parameters.AddWithValue("package_id", job.PackageId);
        command.Parameters.AddWithValue("status", job.Status);
        command.Parameters.AddWithValue("attempts", job.Attempts);
        command.Parameters.AddWithValue("requested_by_api_key_id", (object?) job.RequestedByApiKeyId ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", job.CreatedAtUtc);
        command.Parameters.AddWithValue("started_at", DBNull.Value);
        command.Parameters.AddWithValue("completed_at", DBNull.Value);
        command.Parameters.AddWithValue("last_error", DBNull.Value);
        command.Parameters.AddWithValue("result_json", DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return job;
    }

    /// <inheritdoc />
    public async Task<PackageVerificationJobRecord?> TryStartNextAsync(
        TimeSpan staleRunningThreshold,
        CancellationToken cancellationToken) {
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var staleBefore = DateTimeOffset.UtcNow - staleRunningThreshold;

        await using (var resetCommand = connection.CreateCommand()) {
            resetCommand.CommandText = """
                                       update ows_package_verification_jobs
                                       set status = 'Pending',
                                           started_at = null,
                                           last_error = coalesce(last_error, 'Reset to pending after stale running job timeout.')
                                       where status = 'Running'
                                         and started_at is not null
                                         and started_at <= @stale_before;
                                       """;
            resetCommand.Parameters.AddWithValue("stale_before", staleBefore);
            await resetCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              with candidate as (
                                  select id
                                  from ows_package_verification_jobs
                                  where status = 'Pending'
                                  order by created_at asc, id asc
                                  limit 1
                                  for update skip locked
                              )
                              update ows_package_verification_jobs as jobs
                              set status = 'Running',
                                  attempts = jobs.attempts + 1,
                                  started_at = @started_at,
                                  completed_at = null,
                                  last_error = null
                              from candidate
                              where jobs.id = candidate.id
                              returning jobs.id, jobs.package_id, jobs.status, jobs.attempts, jobs.requested_by_api_key_id, jobs.created_at, jobs.started_at, jobs.completed_at, jobs.last_error, jobs.result_json;
                              """;
        command.Parameters.AddWithValue("started_at", DateTimeOffset.UtcNow);

        PackageVerificationJobRecord? job = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken)) {
            if (await reader.ReadAsync(cancellationToken)) {
                job = ReadJob(reader);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    /// <inheritdoc />
    public async Task<PackageVerificationJobRecord?> GetLatestForPackageAsync(string packageId,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select id, package_id, status, attempts, requested_by_api_key_id, created_at, started_at, completed_at, last_error, result_json
                              from ows_package_verification_jobs
                              where package_id = @package_id
                              order by created_at desc, id desc
                              limit 1;
                              """;
        command.Parameters.AddWithValue("package_id", packageId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadJob(reader) : null;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        string jobId,
        string status,
        string? resultJson,
        string? lastError,
        CancellationToken cancellationToken) {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              update ows_package_verification_jobs
                              set status = @status,
                                  completed_at = @completed_at,
                                  result_json = @result_json,
                                  last_error = @last_error
                              where id = @id;
                              """;
        command.Parameters.AddWithValue("id", jobId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("completed_at", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("result_json", (object?) resultJson ?? DBNull.Value);
        command.Parameters.AddWithValue("last_error", (object?) lastError ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PackageVerificationJobSummary> GetSummaryAsync(CancellationToken cancellationToken) {
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select status, count(*)::int
                              from ows_package_verification_jobs
                              group by status;
                              """;

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return new PackageVerificationJobSummary {
            Pending = counts.GetValueOrDefault("Pending"),
            Running = counts.GetValueOrDefault("Running"),
            Succeeded = counts.GetValueOrDefault("Succeeded"),
            Failed = counts.GetValueOrDefault("Failed")
        };
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    /// <summary>
    /// Reads and maps a single job record from a database data reader.
    /// </summary>
    /// <param name="reader">The active database reader.</param>
    /// <returns>A mapped job record instance.</returns>
    private static PackageVerificationJobRecord ReadJob(NpgsqlDataReader reader) =>
        new() {
            Id = reader.GetString(0),
            PackageId = reader.GetString(1),
            Status = reader.GetString(2),
            Attempts = reader.GetInt32(3),
            RequestedByApiKeyId = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedAtUtc = reader.GetFieldValue<DateTimeOffset>(5),
            StartedAtUtc = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            CompletedAtUtc = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            LastError = reader.IsDBNull(8) ? null : reader.GetString(8),
            ResultJson = reader.IsDBNull(9) ? null : reader.GetString(9)
        };
}
