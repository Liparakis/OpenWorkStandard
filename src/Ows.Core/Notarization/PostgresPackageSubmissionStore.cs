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
        var session = string.IsNullOrWhiteSpace(request.SessionId)
            ? null
            : await GetSessionAnchorAsync(connection, request.SessionId, cancellationToken);
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
                                  package_size_bytes,
                                  session_head_receipt_hash,
                                  session_head_event_hash,
                                  session_checkpoint_count
                              )
                              values (
                                  @id,
                                  @session_id,
                                  @object_storage_provider,
                                  @object_bucket,
                                  @object_key,
                                  @package_sha256,
                                  @package_size_bytes,
                                  @session_head_receipt_hash,
                                  @session_head_event_hash,
                                  @session_checkpoint_count
                              )
                              returning id, session_id, object_storage_provider, object_bucket, object_key, package_sha256, package_size_bytes, session_head_receipt_hash, session_head_event_hash, session_checkpoint_count, verification_status, created_at;
                              """;
        command.Parameters.AddWithValue("id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("session_id", (object?)request.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("object_storage_provider", request.ObjectStorageProvider);
        command.Parameters.AddWithValue("object_bucket", request.ObjectBucket);
        command.Parameters.AddWithValue("object_key", request.ObjectKey);
        command.Parameters.AddWithValue("package_sha256", request.PackageSha256.ToLowerInvariant());
        command.Parameters.AddWithValue("package_size_bytes", request.PackageSizeBytes);
        command.Parameters.AddWithValue("session_head_receipt_hash", (object?)session?.HeadReceiptHash ?? DBNull.Value);
        command.Parameters.AddWithValue("session_head_event_hash", (object?)session?.HeadEventHash ?? DBNull.Value);
        command.Parameters.AddWithValue("session_checkpoint_count", (object?)session?.CheckpointCount ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadSubmission(reader);
    }

    /// <summary>
    /// Gets a package submission by durable identifier.
    /// </summary>
    /// <param name="submissionId">The package submission identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The package submission when found; otherwise <see langword="null"/>.</returns>
    public async Task<VerifierPackageSubmissionResponse?> GetAsync(
        string submissionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(submissionId);
        if (_dataSource is null)
        {
            throw new NotSupportedException("Package submission requires PostgreSQL verifier storage.");
        }

        await _initializationTask.WaitAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select id, session_id, object_storage_provider, object_bucket, object_key, package_sha256, package_size_bytes, session_head_receipt_hash, session_head_event_hash, session_checkpoint_count, verification_status, created_at
                              from verifier_package_submissions
                              where id = @id;
                              """;
        command.Parameters.AddWithValue("id", submissionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSubmission(reader) : null;
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
                              select id, session_id, object_storage_provider, object_bucket, object_key, package_sha256, package_size_bytes, session_head_receipt_hash, session_head_event_hash, session_checkpoint_count, verification_status, created_at
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
    /// Loads the current session head used to anchor a package registration.
    /// </summary>
    /// <param name="connection">The open database connection.</param>
    /// <param name="sessionId">The verifier session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current verifier session anchor.</returns>
    private static async Task<VerifierSessionRecord> GetSessionAnchorAsync(
        NpgsqlConnection connection,
        string sessionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select id, head_receipt_hash, head_event_hash, checkpoint_count
                              from verifier_sessions
                              where id = @id;
                              """;
        command.Parameters.AddWithValue("id", sessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Unknown assessment session: {sessionId}");
        }

        return new VerifierSessionRecord
        {
            Id = new AssessmentSessionId(reader.GetString(0)),
            HeadReceiptHash = reader.GetString(1),
            HeadEventHash = reader.GetString(2),
            CheckpointCount = reader.GetInt32(3)
        };
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
            SessionHeadReceiptHash = reader.IsDBNull(7) ? null : reader.GetString(7),
            SessionHeadEventHash = reader.IsDBNull(8) ? null : reader.GetString(8),
            SessionCheckpointCount = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            VerificationStatus = reader.GetString(10),
            CreatedAtUtc = reader.GetFieldValue<DateTimeOffset>(11)
        };
}
