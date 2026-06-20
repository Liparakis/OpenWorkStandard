using System.Text.Json;
using Npgsql;

namespace Ows.Verifier.Server;

/// <summary>
/// Persists package verification jobs so uploads survive server restarts.
/// </summary>
internal interface IPackageVerificationJobStore
{
    /// <summary>
    /// Initializes the backing store.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Queues a verification job unless one is already pending or running.
    /// </summary>
    Task<PackageVerificationJobRecord> QueueAsync(
        string packageId,
        string? requestedByApiKeyId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Starts the next runnable job and returns it, or null if none is ready.
    /// </summary>
    Task<PackageVerificationJobRecord?> TryStartNextAsync(
        TimeSpan staleRunningThreshold,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the latest job for one package.
    /// </summary>
    Task<PackageVerificationJobRecord?> GetLatestForPackageAsync(string packageId, CancellationToken cancellationToken);

    /// <summary>
    /// Completes a running job.
    /// </summary>
    Task CompleteAsync(
        string jobId,
        string status,
        string? resultJson,
        string? lastError,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns aggregate job counts for diagnostics.
    /// </summary>
    Task<PackageVerificationJobSummary> GetSummaryAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Represents one durable package verification job.
/// </summary>
internal sealed record PackageVerificationJobRecord
{
    public string Id { get; init; } = string.Empty;

    public string PackageId { get; init; } = string.Empty;

    public string Status { get; init; } = "Pending";

    public int Attempts { get; init; }

    public string? RequestedByApiKeyId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string? LastError { get; init; }

    public string? ResultJson { get; init; }
}

/// <summary>
/// Holds aggregate job counters for diagnostics.
/// </summary>
internal sealed record PackageVerificationJobSummary
{
    public int Pending { get; init; }

    public int Running { get; init; }

    public int Succeeded { get; init; }

    public int Failed { get; init; }
}

internal sealed class JsonFilePackageVerificationJobStore : IPackageVerificationJobStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PackageVerificationJobRecord> _jobs = [];
    private readonly string _storePath;

    /// <summary>
    /// Initializes a new JSON job store.
    /// </summary>
    public JsonFilePackageVerificationJobStore(string storePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);
        _storePath = storePath;
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadFromDisk();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PackageVerificationJobRecord> QueueAsync(
        string packageId,
        string? requestedByApiKeyId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var existing = _jobs.Values
                .Where(job => string.Equals(job.PackageId, packageId, StringComparison.Ordinal))
                .OrderByDescending(job => job.CreatedAtUtc)
                .FirstOrDefault();
            if (existing is not null && (existing.Status == "Pending" || existing.Status == "Running"))
            {
                return Task.FromResult(existing);
            }

            var job = new PackageVerificationJobRecord
            {
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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            ResetStaleRunningJobs(now, staleRunningThreshold);

            var next = _jobs.Values
                .Where(job => job.Status == "Pending")
                .OrderBy(job => job.CreatedAtUtc)
                .FirstOrDefault();
            if (next is null)
            {
                return Task.FromResult<PackageVerificationJobRecord?>(null);
            }

            var started = next with
            {
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
    public Task<PackageVerificationJobRecord?> GetLatestForPackageAsync(string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
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
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var existing))
            {
                throw new InvalidOperationException($"Verification job not found: {jobId}");
            }

            _jobs[jobId] = existing with
            {
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
    public Task<PackageVerificationJobSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(BuildSummary(_jobs.Values));
        }
    }

    private void LoadFromDisk()
    {
        lock (_gate)
        {
            _jobs.Clear();
            if (!File.Exists(_storePath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(_storePath);
                var snapshot = JsonSerializer.Deserialize<List<PackageVerificationJobRecord>>(json, SerializerOptions);
                if (snapshot is null)
                {
                    return;
                }

                foreach (var job in snapshot)
                {
                    _jobs[job.Id] = job;
                }
            }
            catch
            {
                // ponytail: malformed local job history starts empty; add repair tooling only if operators ask for it.
            }
        }
    }

    private void SaveToDisk()
    {
        var directory = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _storePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_jobs.Values.ToList(), SerializerOptions));
        File.Move(tempPath, _storePath, overwrite: true);
    }

    private void ResetStaleRunningJobs(DateTimeOffset now, TimeSpan staleRunningThreshold)
    {
        foreach (var job in _jobs.Values.Where(job =>
                     job.Status == "Running" &&
                     job.StartedAtUtc.HasValue &&
                     now - job.StartedAtUtc.Value >= staleRunningThreshold).ToArray())
        {
            _jobs[job.Id] = job with
            {
                Status = "Pending",
                StartedAtUtc = null,
                LastError = "Reset to pending after stale running job timeout."
            };
        }
    }

    private static PackageVerificationJobSummary BuildSummary(IEnumerable<PackageVerificationJobRecord> jobs)
    {
        var counts = jobs.GroupBy(static job => job.Status, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);
        return new PackageVerificationJobSummary
        {
            Pending = counts.GetValueOrDefault("Pending"),
            Running = counts.GetValueOrDefault("Running"),
            Succeeded = counts.GetValueOrDefault("Succeeded"),
            Failed = counts.GetValueOrDefault("Failed")
        };
    }
}

internal sealed class PostgresPackageVerificationJobStore : IPackageVerificationJobStore, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly Task _initializationTask;

    /// <summary>
    /// Initializes a PostgreSQL-backed job store.
    /// </summary>
    public PostgresPackageVerificationJobStore(string connectionString, bool applyMigrationsOnStartup = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _initializationTask = applyMigrationsOnStartup
            ? Ows.Core.Notarization.PostgresVerifierMigrator.MigrateAsync(_dataSource)
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _initializationTask.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PackageVerificationJobRecord> QueueAsync(
        string packageId,
        string? requestedByApiKeyId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await using (var existingCommand = connection.CreateCommand())
        {
            existingCommand.CommandText = """
                                          select id, package_id, status, attempts, requested_by_api_key_id, created_at, started_at, completed_at, last_error, result_json
                                          from ows_package_verification_jobs
                                          where package_id = @package_id
                                          order by created_at desc, id desc
                                          limit 1;
                                          """;
            existingCommand.Parameters.AddWithValue("package_id", packageId);
            await using var existingReader = await existingCommand.ExecuteReaderAsync(cancellationToken);
            if (await existingReader.ReadAsync(cancellationToken))
            {
                var existing = ReadJob(existingReader);
                if (existing.Status is "Pending" or "Running")
                {
                    return existing;
                }
            }
        }

        var job = new PackageVerificationJobRecord
        {
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
        command.Parameters.AddWithValue("requested_by_api_key_id", (object?)job.RequestedByApiKeyId ?? DBNull.Value);
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
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var staleBefore = DateTimeOffset.UtcNow - staleRunningThreshold;

        await using (var resetCommand = connection.CreateCommand())
        {
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
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                job = ReadJob(reader);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    /// <inheritdoc />
    public async Task<PackageVerificationJobRecord?> GetLatestForPackageAsync(string packageId, CancellationToken cancellationToken)
    {
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
        CancellationToken cancellationToken)
    {
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
        command.Parameters.AddWithValue("result_json", (object?)resultJson ?? DBNull.Value);
        command.Parameters.AddWithValue("last_error", (object?)lastError ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PackageVerificationJobSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
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
        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return new PackageVerificationJobSummary
        {
            Pending = counts.GetValueOrDefault("Pending"),
            Running = counts.GetValueOrDefault("Running"),
            Succeeded = counts.GetValueOrDefault("Succeeded"),
            Failed = counts.GetValueOrDefault("Failed")
        };
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private static PackageVerificationJobRecord ReadJob(NpgsqlDataReader reader) =>
        new()
        {
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
