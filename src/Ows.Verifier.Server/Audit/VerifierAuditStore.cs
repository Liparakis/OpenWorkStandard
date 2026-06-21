using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Ows.Verifier.Server;

/// <summary>
/// Represents a single safe audit event.
/// </summary>
public sealed record VerifierAuditEvent {
    /// <summary>
    /// Gets the durable event identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets the event creation time in UTC.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the audit event type.
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional persisted actor key identifier.
    /// </summary>
    public string? ActorKeyId { get; init; }

    /// <summary>
    /// Gets the optional non-secret actor key prefix.
    /// </summary>
    public string? ActorKeyPrefix { get; init; }

    /// <summary>
    /// Gets the optional actor role.
    /// </summary>
    public string? ActorRole { get; init; }

    /// <summary>
    /// Gets the optional actor user identifier.
    /// </summary>
    public string? ActorUserId { get; init; }

    /// <summary>
    /// Gets the optional actor email.
    /// </summary>
    public string? ActorEmail { get; init; }

    /// <summary>
    /// Gets the optional actor display name.
    /// </summary>
    public string? ActorDisplayName { get; init; }

    /// <summary>
    /// Gets the optional institution identifier.
    /// </summary>
    public string? InstitutionId { get; init; }

    /// <summary>
    /// Gets the optional session identifier.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the optional package identifier.
    /// </summary>
    public string? PackageId { get; init; }

    /// <summary>
    /// Gets the optional assessment identifier.
    /// </summary>
    public string? AssessmentId { get; init; }

    /// <summary>
    /// Gets the result label for the event.
    /// </summary>
    public string Result { get; init; } = string.Empty;

    /// <summary>
    /// Gets the safe metadata payload.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Metadata { get; init; } = new Dictionary<string, string?>();
}

/// <summary>
/// Describes supported audit-event filters.
/// </summary>
public sealed record VerifierAuditQuery {
    /// <summary>
    /// Gets the default number of audit events returned when no limit is supplied.
    /// </summary>
    public const int DefaultLimit = 100;

    /// <summary>
    /// Gets the hard upper bound for audit event queries.
    /// </summary>
    public const int MaxLimit = 500;

    /// <summary>
    /// Gets the optional institution identifier filter.
    /// </summary>
    public string? InstitutionId { get; init; }

    /// <summary>
    /// Gets the optional session identifier filter.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the optional package identifier filter.
    /// </summary>
    public string? PackageId { get; init; }

    /// <summary>
    /// Gets the optional event type filter.
    /// </summary>
    public string? EventType { get; init; }

    /// <summary>
    /// Gets the optional lower timestamp bound.
    /// </summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>
    /// Gets the maximum number of returned events.
    /// </summary>
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>
    /// Normalizes a caller-supplied limit into the supported audit query range.
    /// </summary>
    public static int NormalizeLimit(int? limit) =>
        !limit.HasValue || limit.Value <= 0 ? DefaultLimit : Math.Clamp(limit.Value, 1, MaxLimit);
}

/// <summary>
/// Holds aggregate counts derived from persisted audit events.
/// </summary>
public sealed record VerifierAuditSummary {
    /// <summary>Gets the count of session-created events.</summary>
    public int SessionsCreated { get; init; }

    /// <summary>Gets the count of checkpoint-accepted events.</summary>
    public int CheckpointsAccepted { get; init; }

    /// <summary>Gets the count of heartbeat-accepted events.</summary>
    public int HeartbeatsAccepted { get; init; }

    /// <summary>Gets the count of package-submitted events.</summary>
    public int PackagesSubmitted { get; init; }

    /// <summary>Gets the count of package-verified events.</summary>
    public int PackagesVerified { get; init; }

    /// <summary>Gets the count of package-verification-failed events.</summary>
    public int PackageVerificationFailures { get; init; }

    /// <summary>Gets the count of report-read events.</summary>
    public int ReportsRead { get; init; }

    /// <summary>Gets the count of auth-failed events.</summary>
    public int AuthFailures { get; init; }

    /// <summary>Gets the count of access-denied events.</summary>
    public int AccessDenied { get; init; }
}

internal sealed class JsonFileVerifierAuditStore : IVerifierAuditStore {
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly Lock _gate = new();
    private readonly List<VerifierAuditEvent> _events = [];
    private readonly string _storePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonFileVerifierAuditStore"/> class.
    /// </summary>
    public JsonFileVerifierAuditStore(string storePath) {
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
    public Task AppendAsync(VerifierAuditEvent auditEvent, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(auditEvent);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate) {
            _events.Add(auditEvent);
            SaveToDisk();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VerifierAuditEvent>> QueryAsync(VerifierAuditQuery query,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate) {
            IReadOnlyList<VerifierAuditEvent> result = ApplyQuery(_events, query).ToArray();
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<VerifierAuditSummary> GetSummaryAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) {
            return Task.FromResult(BuildSummary(_events));
        }
    }

    /// <summary>
    /// Loads the JSON snapshot from disk.
    /// </summary>
    private void LoadFromDisk() {
        lock (_gate) {
            _events.Clear();
            if (!File.Exists(_storePath)) {
                return;
            }

            try {
                var json = File.ReadAllText(_storePath);
                var snapshot = JsonSerializer.Deserialize<List<VerifierAuditEvent>>(json, SerializerOptions);
                if (snapshot is not null) {
                    _events.AddRange(snapshot);
                }
            } catch {
                // ponytail: malformed local audit history starts empty; add recovery tooling only if operators need it.
            }
        }
    }

    /// <summary>
    /// Saves the JSON snapshot to disk atomically.
    /// </summary>
    private void SaveToDisk() {
        var directory = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _storePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(_events, SerializerOptions));
        File.Move(tempPath, _storePath, overwrite: true);
    }

    /// <summary>
    /// Applies the supported filters and limit.
    /// </summary>
    private static IEnumerable<VerifierAuditEvent> ApplyQuery(
        IEnumerable<VerifierAuditEvent> events,
        VerifierAuditQuery query) {
        var limit = VerifierAuditQuery.NormalizeLimit(query.Limit);
        return events
            .Where(auditEvent => string.IsNullOrWhiteSpace(query.InstitutionId) ||
                                 string.Equals(auditEvent.InstitutionId, query.InstitutionId,
                                     StringComparison.OrdinalIgnoreCase))
            .Where(auditEvent => string.IsNullOrWhiteSpace(query.SessionId) ||
                                 string.Equals(auditEvent.SessionId, query.SessionId,
                                     StringComparison.OrdinalIgnoreCase))
            .Where(auditEvent => string.IsNullOrWhiteSpace(query.PackageId) ||
                                 string.Equals(auditEvent.PackageId, query.PackageId,
                                     StringComparison.OrdinalIgnoreCase))
            .Where(auditEvent => string.IsNullOrWhiteSpace(query.EventType) ||
                                 string.Equals(auditEvent.EventType, query.EventType,
                                     StringComparison.OrdinalIgnoreCase))
            .Where(auditEvent => !query.Since.HasValue || auditEvent.CreatedAtUtc >= query.Since.Value)
            .OrderByDescending(auditEvent => auditEvent.CreatedAtUtc)
            .ThenByDescending(auditEvent => auditEvent.Id)
            .Take(limit);
    }

    /// <summary>
    /// Builds the diagnostics counters from the stored events.
    /// </summary>
    private static VerifierAuditSummary BuildSummary(IEnumerable<VerifierAuditEvent> events) {
        var counts = events.GroupBy(static auditEvent => auditEvent.EventType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);
        return new VerifierAuditSummary {
            SessionsCreated = counts.GetValueOrDefault("session.created"),
            CheckpointsAccepted = counts.GetValueOrDefault("checkpoint.accepted"),
            HeartbeatsAccepted = counts.GetValueOrDefault("heartbeat.accepted"),
            PackagesSubmitted = counts.GetValueOrDefault("package.submitted"),
            PackagesVerified = counts.GetValueOrDefault("package.verified"),
            PackageVerificationFailures = counts.GetValueOrDefault("package.verification.failed"),
            ReportsRead = counts.GetValueOrDefault("report.read"),
            AuthFailures = counts.GetValueOrDefault("auth.failed"),
            AccessDenied = counts.GetValueOrDefault("access.denied")
        };
    }
}

internal sealed class PostgresVerifierAuditStore : IVerifierAuditStore, IAsyncDisposable {
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        PropertyNameCaseInsensitive = true
    };

    private readonly NpgsqlDataSource _dataSource;
    private readonly Task _initializationTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresVerifierAuditStore"/> class.
    /// </summary>
    public PostgresVerifierAuditStore(string connectionString, bool applyMigrationsOnStartup = true) {
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
    public async Task AppendAsync(VerifierAuditEvent auditEvent, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(auditEvent);
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              insert into verifier_audit_events (session_id, event_type, payload_json, created_at)
                              values (@session_id, @event_type, @payload_json::jsonb, @created_at);
                              """;
        command.Parameters.AddWithValue("session_id", (object?) auditEvent.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("event_type", auditEvent.EventType);
        command.Parameters.AddWithValue("payload_json", JsonSerializer.Serialize(auditEvent, SerializerOptions));
        command.Parameters.AddWithValue("created_at", auditEvent.CreatedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VerifierAuditEvent>> QueryAsync(VerifierAuditQuery query,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(query);
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select id, session_id, event_type, payload_json::text, created_at
                              from verifier_audit_events
                              where (@institution_id is null or payload_json ->> 'InstitutionId' = @institution_id)
                                and (@session_id is null or session_id = @session_id)
                                and (@package_id is null or payload_json ->> 'PackageId' = @package_id)
                                and (@event_type is null or event_type = @event_type)
                                and (@since is null or created_at >= @since)
                              order by created_at desc, id desc
                              limit @limit;
                              """;
        command.Parameters.Add(new NpgsqlParameter("institution_id", NpgsqlDbType.Text) {
            Value = (object?) NormalizeFilter(query.InstitutionId) ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("session_id", NpgsqlDbType.Text) {
            Value = (object?) NormalizeFilter(query.SessionId) ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("package_id", NpgsqlDbType.Text) {
            Value = (object?) NormalizeFilter(query.PackageId) ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("event_type", NpgsqlDbType.Text) {
            Value = (object?) NormalizeFilter(query.EventType) ?? DBNull.Value
        });
        command.Parameters.Add(new NpgsqlParameter("since", NpgsqlDbType.TimestampTz) {
            Value = (object?) query.Since ?? DBNull.Value
        });
        command.Parameters.AddWithValue("limit", VerifierAuditQuery.NormalizeLimit(query.Limit));

        var result = new List<VerifierAuditEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) {
            result.Add(ReadAuditEvent(reader));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<VerifierAuditSummary> GetSummaryAsync(CancellationToken cancellationToken) {
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select event_type, count(*)::int
                              from verifier_audit_events
                              group by event_type;
                              """;

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return new VerifierAuditSummary {
            SessionsCreated = counts.GetValueOrDefault("session.created"),
            CheckpointsAccepted = counts.GetValueOrDefault("checkpoint.accepted"),
            HeartbeatsAccepted = counts.GetValueOrDefault("heartbeat.accepted"),
            PackagesSubmitted = counts.GetValueOrDefault("package.submitted"),
            PackagesVerified = counts.GetValueOrDefault("package.verified"),
            PackageVerificationFailures = counts.GetValueOrDefault("package.verification.failed"),
            ReportsRead = counts.GetValueOrDefault("report.read"),
            AuthFailures = counts.GetValueOrDefault("auth.failed"),
            AccessDenied = counts.GetValueOrDefault("access.denied")
        };
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    /// <summary>
    /// Normalizes an optional filter value.
    /// </summary>
    private static string? NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Rehydrates an audit event from the row payload.
    /// </summary>
    private static VerifierAuditEvent ReadAuditEvent(NpgsqlDataReader reader) {
        var payload = JsonSerializer.Deserialize<VerifierAuditEvent>(reader.GetString(3), SerializerOptions)
                      ?? new VerifierAuditEvent();
        return payload with {
            Id = reader.GetInt64(0).ToString(),
            SessionId = reader.IsDBNull(1) ? payload.SessionId : reader.GetString(1),
            EventType = reader.GetString(2),
            CreatedAtUtc = reader.GetFieldValue<DateTimeOffset>(4)
        };
    }
}
