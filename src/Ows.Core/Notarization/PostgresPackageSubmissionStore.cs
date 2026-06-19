using Npgsql;

namespace Ows.Core.Notarization;

/// <summary>
/// Persists package object metadata in PostgreSQL while package bytes remain in object storage.
/// </summary>
public sealed class PostgresPackageSubmissionStore : IAsyncDisposable
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly Task _initializationTask;

    /// <summary>
    /// Initializes a disabled package submission store for non-PostgreSQL verifier modes.
    /// </summary>
    public PostgresPackageSubmissionStore()
    {
        _initializationTask = Task.CompletedTask;
    }

    /// <summary>
    /// Initializes a PostgreSQL-backed package submission store.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    public PostgresPackageSubmissionStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _dataSource = NpgsqlDataSource.Create(connectionString);
        _initializationTask = PostgresVerifierMigrator.MigrateAsync(_dataSource);
    }

    /// <summary>
    /// Registers an object-store-backed package submission without storing package bytes in PostgreSQL.
    /// </summary>
    /// <param name="request">The package submission metadata.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The durable package submission record.</returns>
    public async Task<VerifierPackageSubmissionResponse> SubmitAsync(
        VerifierPackageSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_dataSource is null)
        {
            throw new NotSupportedException("Package submission requires PostgreSQL verifier storage.");
        }

        var validationError = request.GetValidationError();
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        await _initializationTask.WaitAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var existing = await TryGetExistingAsync(connection, request, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.PackageSha256, request.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
                existing.PackageSizeBytes != request.PackageSizeBytes)
            {
                throw new InvalidOperationException("Package object is already registered with different metadata.");
            }

            return existing;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              insert into verifier_package_submissions (
                                  id,
                                  session_id,
                                  object_storage_provider,
                                  object_bucket,
                                  object_key,
                                  package_sha256,
                                  package_size_bytes
                              )
                              values (
                                  @id,
                                  @session_id,
                                  @object_storage_provider,
                                  @object_bucket,
                                  @object_key,
                                  @package_sha256,
                                  @package_size_bytes
                              )
                              returning id, session_id, object_storage_provider, object_bucket, object_key, package_sha256, package_size_bytes, verification_status, created_at;
                              """;
        command.Parameters.AddWithValue("id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("session_id", (object?)request.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("object_storage_provider", request.ObjectStorageProvider);
        command.Parameters.AddWithValue("object_bucket", request.ObjectBucket);
        command.Parameters.AddWithValue("object_key", request.ObjectKey);
        command.Parameters.AddWithValue("package_sha256", request.PackageSha256.ToLowerInvariant());
        command.Parameters.AddWithValue("package_size_bytes", request.PackageSizeBytes);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadSubmission(reader);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _dataSource?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// Loads an existing package submission by immutable object storage location.
    /// </summary>
    /// <param name="connection">The open database connection.</param>
    /// <param name="request">The package submission request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The existing submission when found; otherwise <see langword="null"/>.</returns>
    private static async Task<VerifierPackageSubmissionResponse?> TryGetExistingAsync(
        NpgsqlConnection connection,
        VerifierPackageSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select id, session_id, object_storage_provider, object_bucket, object_key, package_sha256, package_size_bytes, verification_status, created_at
                              from verifier_package_submissions
                              where object_storage_provider = @object_storage_provider
                                and object_bucket = @object_bucket
                                and object_key = @object_key;
                              """;
        command.Parameters.AddWithValue("object_storage_provider", request.ObjectStorageProvider);
        command.Parameters.AddWithValue("object_bucket", request.ObjectBucket);
        command.Parameters.AddWithValue("object_key", request.ObjectKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSubmission(reader) : null;
    }

    /// <summary>
    /// Maps a package submission row to the public response model.
    /// </summary>
    /// <param name="reader">The active row reader.</param>
    /// <returns>The mapped package submission response.</returns>
    private static VerifierPackageSubmissionResponse ReadSubmission(NpgsqlDataReader reader) =>
        new()
        {
            SubmissionId = reader.GetString(0),
            SessionId = reader.IsDBNull(1) ? null : reader.GetString(1),
            ObjectStorageProvider = reader.GetString(2),
            ObjectBucket = reader.GetString(3),
            ObjectKey = reader.GetString(4),
            PackageSha256 = reader.GetString(5),
            PackageSizeBytes = reader.GetInt64(6),
            VerificationStatus = reader.GetString(7),
            CreatedAtUtc = reader.GetFieldValue<DateTimeOffset>(8)
        };
}
