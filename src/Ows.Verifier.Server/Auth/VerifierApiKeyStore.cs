using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using Ows.Core.Notarization;

namespace Ows.Verifier.Server;

/// <summary>
/// Describes the request used to create a persisted verifier API key.
/// </summary>
/// <param name="Role">The requested verifier role.</param>
/// <param name="InstitutionId">The optional institution scope.</param>
/// <param name="StudentUserId">The optional student user ID binding (only for StudentClient keys).</param>
/// <param name="ExpiresAtUtc">The optional UTC expiry timestamp.</param>
public sealed record VerifierApiKeyCreateRequest(
    string Role = "Operator",
    string? InstitutionId = null,
    string? StudentUserId = null,
    DateTimeOffset? ExpiresAtUtc = null)
{
    /// <summary>
    /// Returns the validation error, if any.
    /// </summary>
    public string? GetValidationError()
    {
        if (!VerifierRolePolicy.IsSupportedRole(Role))
        {
            return "Role must be Operator, InstitutionAdmin, InstructorReviewer, or StudentClient.";
        }

        if (VerifierRolePolicy.IsInstitutionScopedRole(Role) && string.IsNullOrWhiteSpace(InstitutionId))
        {
            return "InstitutionId is required for InstitutionAdmin, InstructorReviewer, and StudentClient keys.";
        }

        return null;
    }
}

/// <summary>
/// Returns the raw key once together with its durable metadata.
/// </summary>
/// <param name="ApiKey">The generated raw API key. Returned once and never stored.</param>
/// <param name="Metadata">The persisted metadata for the new API key.</param>
public sealed record VerifierApiKeyCreateResult(
    string ApiKey,
    VerifierApiKeyMetadata Metadata);

/// <summary>
/// Represents the stored metadata for a verifier API key.
/// </summary>
/// <param name="KeyId">The durable API key identifier.</param>
/// <param name="KeyPrefix">The non-secret display prefix.</param>
/// <param name="Role">The verifier role.</param>
/// <param name="InstitutionId">The optional institution scope.</param>
/// <param name="StudentUserId">The optional student user ID binding.</param>
/// <param name="CreatedAtUtc">The UTC creation timestamp.</param>
/// <param name="ExpiresAtUtc">The optional UTC expiry timestamp.</param>
/// <param name="LastUsedAtUtc">The optional UTC timestamp when the key was last used successfully.</param>
/// <param name="RevokedAtUtc">The optional UTC revocation timestamp.</param>
public sealed record VerifierApiKeyMetadata(
    string KeyId,
    string KeyPrefix,
    string Role,
    string? InstitutionId,
    string? StudentUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? RevokedAtUtc);

/// <summary>
/// Represents a persisted verifier API key record.
/// </summary>
internal sealed record PersistedVerifierApiKeyRecord
{
    /// <summary>
    /// Gets the unique key identifier.
    /// </summary>
    public string KeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the non-secret key prefix.
    /// </summary>
    public string KeyPrefix { get; init; } = string.Empty;

    /// <summary>
    /// Gets the SHA-256 hash of the API key.
    /// </summary>
    public string KeyHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the security role.
    /// </summary>
    public string Role { get; init; } = "Operator";

    /// <summary>
    /// Gets the optional institution identifier scope.
    /// </summary>
    public string? InstitutionId { get; init; }

    /// <summary>
    /// Gets the optional student user identifier.
    /// </summary>
    public string? StudentUserId { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Gets the optional UTC expiry timestamp.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    /// <summary>
    /// Gets the optional UTC last used timestamp.
    /// </summary>
    public DateTimeOffset? LastUsedAtUtc { get; init; }

    /// <summary>
    /// Gets the optional UTC revocation timestamp.
    /// </summary>
    public DateTimeOffset? RevokedAtUtc { get; init; }

    /// <summary>
    /// Converts this persisted record into an API key metadata record.
    /// </summary>
    /// <returns>A mapped <see cref="VerifierApiKeyMetadata"/> instance.</returns>
    public VerifierApiKeyMetadata ToMetadata() =>
        new(KeyId, KeyPrefix, Role, InstitutionId, StudentUserId, CreatedAtUtc, ExpiresAtUtc, LastUsedAtUtc,
            RevokedAtUtc);

    /// <summary>
    /// Checks if the API key is active at a specific timestamp.
    /// </summary>
    /// <param name="now">The timestamp to evaluate key active state against.</param>
    /// <returns>True if the key has not been revoked and is not expired; otherwise, false.</returns>
    public bool IsActiveAt(DateTimeOffset now) =>
        RevokedAtUtc is null && (ExpiresAtUtc is null || ExpiresAtUtc > now);
}

/// <summary>
/// Persists verifier API keys in a local JSON snapshot file.
/// </summary>
internal sealed class JsonFileVerifierApiKeyStore : IVerifierApiKeyStore
{
    /// <summary>
    /// The JSON serialization options.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>
    /// The lock object used to synchronize database operations.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The cached dictionary of API keys keyed by their KeyId.
    /// </summary>
    private readonly Dictionary<string, PersistedVerifierApiKeyRecord> _records = [];

    /// <summary>
    /// The file path to the JSON storage file.
    /// </summary>
    private readonly string _storePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonFileVerifierApiKeyStore"/> class.
    /// </summary>
    public JsonFileVerifierApiKeyStore(string storePath)
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
    public Task<bool> HasActiveKeysAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(_records.Values.Any(record => record.IsActiveAt(now)));
        }
    }

    /// <inheritdoc />
    public Task<VerifierApiKeyCreateResult> CreateAsync(
        VerifierApiKeyCreateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var validationError = request.GetValidationError();
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        var rawKey = VerifierApiKeyMaterial.CreateRawApiKey();
        var now = DateTimeOffset.UtcNow;
        var record = new PersistedVerifierApiKeyRecord
        {
            KeyId = Guid.NewGuid().ToString("N"),
            KeyPrefix = VerifierApiKeyMaterial.CreateKeyPrefix(rawKey),
            KeyHash = VerifierApiKeyMaterial.ComputeKeyHash(rawKey),
            Role = VerifierRolePolicy.NormalizeRoleName(request.Role),
            InstitutionId = VerifierRolePolicy.NormalizeInstitutionId(request.InstitutionId),
            StudentUserId = request.StudentUserId?.Trim(),
            CreatedAtUtc = now,
            ExpiresAtUtc = request.ExpiresAtUtc
        };

        lock (_gate)
        {
            _records[record.KeyId] = record;
            SaveToDisk();
        }

        return Task.FromResult(new VerifierApiKeyCreateResult(rawKey, record.ToMetadata()));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VerifierApiKeyMetadata>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<VerifierApiKeyMetadata> records = _records.Values
                .OrderByDescending(record => record.CreatedAtUtc)
                .Select(record => record.ToMetadata())
                .ToArray();
            return Task.FromResult(records);
        }
    }

    /// <inheritdoc />
    public Task<bool> RevokeAsync(string keyId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_records.TryGetValue(keyId, out var record))
            {
                return Task.FromResult(false);
            }

            if (record.RevokedAtUtc is not null)
            {
                return Task.FromResult(true);
            }

            _records[keyId] = record with { RevokedAtUtc = DateTimeOffset.UtcNow };
            SaveToDisk();
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<VerifierAccessContext?> AuthenticateAsync(string rawApiKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawApiKey);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var keyHash = VerifierApiKeyMaterial.ComputeKeyHash(rawApiKey);
            var record = _records.Values.FirstOrDefault(candidate =>
                string.Equals(candidate.KeyHash, keyHash, StringComparison.Ordinal));
            if (record is null || !record.IsActiveAt(now))
            {
                return Task.FromResult<VerifierAccessContext?>(null);
            }

            _records[record.KeyId] = record with { LastUsedAtUtc = now };
            SaveToDisk();
            return Task.FromResult<VerifierAccessContext?>(new VerifierAccessContext(
                record.Role,
                record.InstitutionId,
                rawApiKey,
                record.KeyId,
                record.KeyPrefix,
                record.StudentUserId));
        }
    }

    /// <summary>
    /// Loads persisted API keys from the JSON snapshot file.
    /// </summary>
    private void LoadFromDisk()
    {
        lock (_gate)
        {
            _records.Clear();
            if (!File.Exists(_storePath))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(_storePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var snapshot = JsonSerializer.Deserialize<List<PersistedVerifierApiKeyRecord>>(json, SerializerOptions);
                if (snapshot is null)
                {
                    return;
                }

                foreach (var record in snapshot)
                {
                    _records[record.KeyId] = record;
                }
            }
            catch
            {
                // ponytail: start empty on malformed local auth state; add operator recovery tooling only if this becomes operationally noisy.
            }
        }
    }

    /// <summary>
    /// Saves the current API key collection to the JSON snapshot file.
    /// </summary>
    private void SaveToDisk()
    {
        var dir = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = _storePath + ".tmp";
        var json = JsonSerializer.Serialize(_records.Values.ToList(), SerializerOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _storePath, overwrite: true);
    }
}

/// <summary>
/// Persists verifier API keys in PostgreSQL.
/// </summary>
internal sealed class PostgresVerifierApiKeyStore : IVerifierApiKeyStore, IAsyncDisposable
{
    /// <summary>
    /// The connection pool database data source.
    /// </summary>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// The background initialization task running migrations.
    /// </summary>
    private readonly Task _initializationTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresVerifierApiKeyStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="applyMigrationsOnStartup">Whether schema migration should run automatically on startup.</param>
    public PostgresVerifierApiKeyStore(string connectionString, bool applyMigrationsOnStartup = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _initializationTask = applyMigrationsOnStartup
            ? PostgresVerifierMigrator.MigrateAsync(_dataSource)
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _initializationTask.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveKeysAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select exists(
                                  select 1
                                  from verifier_api_keys
                                  where revoked_at is null
                                    and (expires_at is null or expires_at > now())
                              );
                              """;
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <inheritdoc />
    public async Task<VerifierApiKeyCreateResult> CreateAsync(
        VerifierApiKeyCreateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validationError = request.GetValidationError();
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        await InitializeAsync(cancellationToken);
        var rawKey = VerifierApiKeyMaterial.CreateRawApiKey();
        var record = new PersistedVerifierApiKeyRecord
        {
            KeyId = Guid.NewGuid().ToString("N"),
            KeyPrefix = VerifierApiKeyMaterial.CreateKeyPrefix(rawKey),
            KeyHash = VerifierApiKeyMaterial.ComputeKeyHash(rawKey),
            Role = VerifierRolePolicy.NormalizeRoleName(request.Role),
            InstitutionId = VerifierRolePolicy.NormalizeInstitutionId(request.InstitutionId),
            StudentUserId = request.StudentUserId?.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = request.ExpiresAtUtc
        };

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              insert into verifier_api_keys (
                                  id,
                                  key_prefix,
                                  key_hash,
                                  role,
                                  institution_id,
                                  student_user_id,
                                  created_at,
                                  expires_at,
                                  last_used_at,
                                  revoked_at
                              )
                              values (
                                  @id,
                                  @key_prefix,
                                  @key_hash,
                                  @role,
                                  @institution_id,
                                  @student_user_id,
                                  @created_at,
                                  @expires_at,
                                  @last_used_at,
                                  @revoked_at
                              );
                              """;
        command.Parameters.AddWithValue("id", record.KeyId);
        command.Parameters.AddWithValue("key_prefix", record.KeyPrefix);
        command.Parameters.AddWithValue("key_hash", record.KeyHash);
        command.Parameters.AddWithValue("role", record.Role);
        command.Parameters.AddWithValue("institution_id", (object?)record.InstitutionId ?? DBNull.Value);
        command.Parameters.AddWithValue("student_user_id", (object?)record.StudentUserId ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", record.CreatedAtUtc);
        command.Parameters.AddWithValue("expires_at", (object?)record.ExpiresAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("last_used_at", DBNull.Value);
        command.Parameters.AddWithValue("revoked_at", DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new VerifierApiKeyCreateResult(rawKey, record.ToMetadata());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VerifierApiKeyMetadata>> ListAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select id, key_prefix, key_hash, role, institution_id, created_at, expires_at, last_used_at, revoked_at, student_user_id
                              from verifier_api_keys
                              order by created_at desc, id desc;
                              """;

        var records = new List<VerifierApiKeyMetadata>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadRecord(reader).ToMetadata());
        }

        return records;
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(string keyId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              update verifier_api_keys
                              set revoked_at = coalesce(revoked_at, @revoked_at)
                              where id = @id;
                              """;
        command.Parameters.AddWithValue("id", keyId);
        command.Parameters.AddWithValue("revoked_at", DateTimeOffset.UtcNow);
        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        return affectedRows > 0;
    }

    /// <inheritdoc />
    public async Task<VerifierAccessContext?> AuthenticateAsync(string rawApiKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawApiKey);
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var keyHash = VerifierApiKeyMaterial.ComputeKeyHash(rawApiKey);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select id, key_prefix, key_hash, role, institution_id, created_at, expires_at, last_used_at, revoked_at, student_user_id
                              from verifier_api_keys
                              where key_hash = @key_hash;
                              """;
        command.Parameters.AddWithValue("key_hash", keyHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var record = ReadRecord(reader);
        await reader.CloseAsync();
        if (!record.IsActiveAt(DateTimeOffset.UtcNow))
        {
            return null;
        }

        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText = """
                                    update verifier_api_keys
                                    set last_used_at = @last_used_at
                                    where id = @id;
                                    """;
        updateCommand.Parameters.AddWithValue("id", record.KeyId);
        updateCommand.Parameters.AddWithValue("last_used_at", DateTimeOffset.UtcNow);
        await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        return new VerifierAccessContext(record.Role, record.InstitutionId, rawApiKey, record.KeyId, record.KeyPrefix,
            record.StudentUserId);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    /// <summary>
    /// Reads and maps a single API key record from a database reader.
    /// </summary>
    /// <param name="reader">The active database reader.</param>
    /// <returns>A mapped persisted API key record instance.</returns>
    private static PersistedVerifierApiKeyRecord ReadRecord(NpgsqlDataReader reader) =>
        new()
        {
            KeyId = reader.GetString(0),
            KeyPrefix = reader.GetString(1),
            KeyHash = reader.GetString(2),
            Role = reader.GetString(3),
            InstitutionId = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedAtUtc = reader.GetFieldValue<DateTimeOffset>(5),
            ExpiresAtUtc = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            LastUsedAtUtc = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            RevokedAtUtc = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            StudentUserId = reader.IsDBNull(9) ? null : reader.GetString(9)
        };
}

/// <summary>
/// Provides static cryptographic helpers for generating and hashing OWS API keys.
/// </summary>
file static class VerifierApiKeyMaterial
{
    /// <summary>
    /// Generates a cryptographically strong random API key prefixed with "ows_".
    /// </summary>
    /// <returns>The generated raw API key string.</returns>
    public static string CreateRawApiKey()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return $"ows_{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    /// <summary>
    /// Creates a safe non-secret prefix from the raw API key.
    /// </summary>
    /// <param name="rawApiKey">The raw API key.</param>
    /// <returns>The non-secret safe prefix.</returns>
    public static string CreateKeyPrefix(string rawApiKey) =>
        rawApiKey[..Math.Min(12, rawApiKey.Length)];

    /// <summary>
    /// Computes the hex-encoded SHA-256 hash of the raw API key.
    /// </summary>
    /// <param name="rawApiKey">The raw API key.</param>
    /// <returns>A hex-encoded lowercase SHA-256 hash string.</returns>
    public static string ComputeKeyHash(string rawApiKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawApiKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}