using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace Ows.Verifier.Server;

/// <summary>
/// Persists verifier API keys without storing raw key material.
/// </summary>
internal interface IVerifierApiKeyStore
{
    /// <summary>
    /// Initializes the backing store.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns whether any non-revoked, non-expired persisted keys exist.
    /// </summary>
    Task<bool> HasActiveKeysAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new persisted API key and returns the raw secret once.
    /// </summary>
    Task<VerifierApiKeyCreateResult> CreateAsync(
        VerifierApiKeyCreateRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists persisted API key metadata without revealing raw secrets.
    /// </summary>
    Task<IReadOnlyList<VerifierApiKeyMetadata>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Revokes a persisted API key.
    /// </summary>
    Task<bool> RevokeAsync(string keyId, CancellationToken cancellationToken);

    /// <summary>
    /// Authenticates a raw API key against persisted key records.
    /// </summary>
    Task<VerifierAccessContext?> AuthenticateAsync(string rawApiKey, CancellationToken cancellationToken);
}

/// <summary>
/// Describes the request used to create a persisted verifier API key.
/// </summary>
public sealed record VerifierApiKeyCreateRequest
{
    /// <summary>
    /// Gets the requested verifier role.
    /// </summary>
    public string Role { get; init; } = "Operator";

    /// <summary>
    /// Gets the optional institution scope.
    /// </summary>
    public string? InstitutionId { get; init; }

    /// <summary>
    /// Gets the optional UTC expiry timestamp.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    /// <summary>
    /// Returns the validation error, if any.
    /// </summary>
    public string? GetValidationError()
    {
        if (!VerifierRolePolicy.IsSupportedRole(Role))
        {
            return "Role must be Operator or InstructorReviewer.";
        }

        if (VerifierRolePolicy.IsInstructorReviewerRole(Role) && string.IsNullOrWhiteSpace(InstitutionId))
        {
            return "InstitutionId is required for InstructorReviewer keys.";
        }

        return null;
    }
}

/// <summary>
/// Returns the raw key once together with its durable metadata.
/// </summary>
public sealed record VerifierApiKeyCreateResult
{
    /// <summary>
    /// Gets the generated raw API key. This value is returned once and not stored.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets the persisted metadata for the new API key.
    /// </summary>
    public VerifierApiKeyMetadata Metadata { get; init; } = new();
}

/// <summary>
/// Represents the stored metadata for a verifier API key.
/// </summary>
public sealed record VerifierApiKeyMetadata
{
    /// <summary>
    /// Gets the durable API key identifier.
    /// </summary>
    public string KeyId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the non-secret display prefix.
    /// </summary>
    public string KeyPrefix { get; init; } = string.Empty;

    /// <summary>
    /// Gets the verifier role.
    /// </summary>
    public string Role { get; init; } = "Operator";

    /// <summary>
    /// Gets the optional institution scope.
    /// </summary>
    public string? InstitutionId { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Gets the optional UTC expiry timestamp.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    /// <summary>
    /// Gets the optional UTC timestamp when the key was last used successfully.
    /// </summary>
    public DateTimeOffset? LastUsedAtUtc { get; init; }

    /// <summary>
    /// Gets the optional UTC revocation timestamp.
    /// </summary>
    public DateTimeOffset? RevokedAtUtc { get; init; }
}

internal sealed record PersistedVerifierApiKeyRecord
{
    public string KeyId { get; init; } = string.Empty;

    public string KeyPrefix { get; init; } = string.Empty;

    public string KeyHash { get; init; } = string.Empty;

    public string Role { get; init; } = "Operator";

    public string? InstitutionId { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public DateTimeOffset? LastUsedAtUtc { get; init; }

    public DateTimeOffset? RevokedAtUtc { get; init; }

    public VerifierApiKeyMetadata ToMetadata() =>
        new()
        {
            KeyId = KeyId,
            KeyPrefix = KeyPrefix,
            Role = Role,
            InstitutionId = InstitutionId,
            CreatedAtUtc = CreatedAtUtc,
            ExpiresAtUtc = ExpiresAtUtc,
            LastUsedAtUtc = LastUsedAtUtc,
            RevokedAtUtc = RevokedAtUtc
        };

    public bool IsActiveAt(DateTimeOffset now) =>
        RevokedAtUtc is null && (ExpiresAtUtc is null || ExpiresAtUtc > now);
}

/// <summary>
/// Persists verifier API keys in a local JSON snapshot file.
/// </summary>
internal sealed class JsonFileVerifierApiKeyStore : IVerifierApiKeyStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PersistedVerifierApiKeyRecord> _records = [];
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
            CreatedAtUtc = now,
            ExpiresAtUtc = request.ExpiresAtUtc
        };

        lock (_gate)
        {
            _records[record.KeyId] = record;
            SaveToDisk();
        }

        return Task.FromResult(new VerifierApiKeyCreateResult
        {
            ApiKey = rawKey,
            Metadata = record.ToMetadata()
        });
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
                record.KeyPrefix));
        }
    }

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
    private readonly NpgsqlDataSource _dataSource;
    private readonly Task _initializationTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresVerifierApiKeyStore"/> class.
    /// </summary>
    public PostgresVerifierApiKeyStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _initializationTask = Ows.Core.Notarization.PostgresVerifierMigrator.MigrateAsync(_dataSource);
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
        command.Parameters.AddWithValue("created_at", record.CreatedAtUtc);
        command.Parameters.AddWithValue("expires_at", (object?)record.ExpiresAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("last_used_at", DBNull.Value);
        command.Parameters.AddWithValue("revoked_at", DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new VerifierApiKeyCreateResult
        {
            ApiKey = rawKey,
            Metadata = record.ToMetadata()
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VerifierApiKeyMetadata>> ListAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select id, key_prefix, key_hash, role, institution_id, created_at, expires_at, last_used_at, revoked_at
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
                              select id, key_prefix, key_hash, role, institution_id, created_at, expires_at, last_used_at, revoked_at
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
        return new VerifierAccessContext(record.Role, record.InstitutionId, rawApiKey, record.KeyId, record.KeyPrefix);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

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
            RevokedAtUtc = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8)
        };
}

file static class VerifierApiKeyMaterial
{
    public static string CreateRawApiKey()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return $"ows_{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    public static string CreateKeyPrefix(string rawApiKey) =>
        rawApiKey[..Math.Min(12, rawApiKey.Length)];

    public static string ComputeKeyHash(string rawApiKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawApiKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
