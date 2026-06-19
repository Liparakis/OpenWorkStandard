using Npgsql;

namespace Ows.Core.Notarization;

/// <summary>
/// Persists package object metadata in PostgreSQL while package bytes remain in object storage.
/// </summary>
public sealed class PostgresPackageSubmissionStore : IPackageSubmissionStore, IAsyncDisposable
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
        var existingByIdempotencyKey = await TryGetExistingByIdempotencyKeyAsync(connection, request, cancellationToken);
        if (existingByIdempotencyKey is not null)
        {
            if (!MatchesRequest(existingByIdempotencyKey, request))
            {
                throw new InvalidOperationException("Package idempotency key already exists with different metadata.");
            }

            return existingByIdempotencyKey;
        }

        var existing = await TryGetExistingAsync(connection, request, cancellationToken);
        if (existing is not null)
        {
            if (!MatchesRequest(existing, request))
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
                                  idempotency_key,
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
                                  @idempotency_key,
                                  @session_head_receipt_hash,
                                  @session_head_event_hash,
                                  @session_checkpoint_count
                              )
                              returning id, session_id, idempotency_key, object_storage_provider, object_bucket, object_key, package_sha256, package_size_bytes, session_head_receipt_hash, session_head_event_hash, session_checkpoint_count, verification_status, created_at, trust_status, verification_result_json;
                              """;
        command.Parameters.AddWithValue("id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("session_id", (object?)request.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("object_storage_provider", request.ObjectStorageProvider);
        command.Parameters.AddWithValue("object_bucket", request.ObjectBucket);
        command.Parameters.AddWithValue("object_key", request.ObjectKey);
        command.Parameters.AddWithValue("package_sha256", request.PackageSha256.ToLowerInvariant());
        command.Parameters.AddWithValue("package_size_bytes", request.PackageSizeBytes);
        command.Parameters.AddWithValue("idempotency_key", (object?)request.IdempotencyKey ?? DBNull.Value);
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
                              select id, session_id, idempotency_key, object_storage_provider, object_bucket, object_key, package_sha256, package_size_bytes, session_head_receipt_hash, session_head_event_hash, session_checkpoint_count, verification_status, created_at, trust_status, verification_result_json
                              from verifier_package_submissions
                              where id = @id;
                              """;
        command.Parameters.AddWithValue("id", submissionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSubmission(reader) : null;
    }

    /// <inheritdoc />
    public async Task UpdateVerificationResultAsync(
        string submissionId,
        string verificationStatus,
        string trustStatus,
        string verificationResultJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(submissionId);
        if (_dataSource is null)
        {
            throw new NotSupportedException("Package verification requires PostgreSQL verifier storage.");
        }

        await _initializationTask.WaitAsync(cancellationToken);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              update verifier_package_submissions
                              set verification_status = @verification_status,
                                  trust_status = @trust_status,
                                  verification_result_json = @verification_result_json
                              where id = @id;
                              """;
        command.Parameters.AddWithValue("id", submissionId);
        command.Parameters.AddWithValue("verification_status", verificationStatus);
        command.Parameters.AddWithValue("trust_status", trustStatus);
        command.Parameters.AddWithValue("verification_result_json", verificationResultJson);

        await command.ExecuteNonQueryAsync(cancellationToken);
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
                              select id, session_id, idempotency_key, object_storage_provider, object_bucket, object_key, package_sha256, package_size_bytes, session_head_receipt_hash, session_head_event_hash, session_checkpoint_count, verification_status, created_at, trust_status, verification_result_json
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
    /// Loads an existing package submission by idempotency key.
    /// </summary>
    /// <param name="connection">The open database connection.</param>
    /// <param name="request">The package submission request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The existing submission when found; otherwise <see langword="null"/>.</returns>
    private static async Task<VerifierPackageSubmissionResponse?> TryGetExistingByIdempotencyKeyAsync(
        NpgsqlConnection connection,
        VerifierPackageSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return null;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select id, session_id, idempotency_key, object_storage_provider, object_bucket, object_key, package_sha256, package_size_bytes, session_head_receipt_hash, session_head_event_hash, session_checkpoint_count, verification_status, created_at, trust_status, verification_result_json
                              from verifier_package_submissions
                              where idempotency_key = @idempotency_key;
                              """;
        command.Parameters.AddWithValue("idempotency_key", request.IdempotencyKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSubmission(reader) : null;
    }

    /// <summary>
    /// Checks whether an existing package submission represents the same request payload.
    /// </summary>
    /// <param name="existing">The existing package submission.</param>
    /// <param name="request">The new package submission request.</param>
    /// <returns><see langword="true"/> when the request payload matches; otherwise, <see langword="false"/>.</returns>
    private static bool MatchesRequest(VerifierPackageSubmissionResponse existing, VerifierPackageSubmissionRequest request) =>
        string.Equals(existing.SessionId, request.SessionId, StringComparison.Ordinal) &&
        string.Equals(existing.ObjectStorageProvider, request.ObjectStorageProvider, StringComparison.Ordinal) &&
        string.Equals(existing.ObjectBucket, request.ObjectBucket, StringComparison.Ordinal) &&
        string.Equals(existing.ObjectKey, request.ObjectKey, StringComparison.Ordinal) &&
        string.Equals(existing.PackageSha256, request.PackageSha256, StringComparison.OrdinalIgnoreCase) &&
        existing.PackageSizeBytes == request.PackageSizeBytes;

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
            IdempotencyKey = reader.IsDBNull(2) ? null : reader.GetString(2),
            ObjectStorageProvider = reader.GetString(3),
            ObjectBucket = reader.GetString(4),
            ObjectKey = reader.GetString(5),
            PackageSha256 = reader.GetString(6),
            PackageSizeBytes = reader.GetInt64(7),
            SessionHeadReceiptHash = reader.IsDBNull(8) ? null : reader.GetString(8),
            SessionHeadEventHash = reader.IsDBNull(9) ? null : reader.GetString(9),
            SessionCheckpointCount = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            VerificationStatus = reader.GetString(11),
            TrustStatus = reader.IsDBNull(13) ? null : reader.GetString(13),
            VerificationResultJson = reader.IsDBNull(14) ? null : reader.GetString(14),
            CreatedAtUtc = reader.GetFieldValue<DateTimeOffset>(12)
        };
}
