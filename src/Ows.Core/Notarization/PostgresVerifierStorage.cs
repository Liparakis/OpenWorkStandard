using Npgsql;

namespace Ows.Core.Notarization;

/// <summary>
/// Persists verifier sessions and checkpoints in PostgreSQL using transactional append semantics.
/// </summary>
public sealed class PostgresVerifierStorage : IVerifierStorage, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly Task _initializationTask;
    private readonly string? _signingKey;

    /// <summary>
    /// Initializes a new PostgreSQL-backed verifier storage instance.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="signingKey">The optional server signing key used to sign issued receipts.</param>
    public PostgresVerifierStorage(string connectionString, string? signingKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _signingKey = signingKey;
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _initializationTask = PostgresVerifierMigrator.MigrateAsync(_dataSource);
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await AwaitInitializationAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            await InitializeAsync(cancellationToken);
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "select 1;";
            var res = await command.ExecuteScalarAsync(cancellationToken);
            return res is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<VerifierSessionRecord> CreateSessionAsync(CancellationToken cancellationToken)
    {
        await AwaitInitializationAsync(cancellationToken);

        var sessionId = AssessmentSessionId.Create();
        var createdAtUtc = DateTimeOffset.UtcNow;
        var leaseExpiresAt = createdAtUtc.Add(TimeSpan.FromSeconds(120));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              insert into verifier_sessions (
                                  id,
                                  created_at,
                                  metadata_json,
                                  head_receipt_hash,
                                  head_event_hash,
                                  checkpoint_count,
                                  last_heartbeat_at,
                                  lease_expires_at,
                                  has_lease_gap,
                                  max_lease_gap_seconds
                              )
                              values (
                                  @id,
                                  @created_at,
                                  cast(@metadata_json as jsonb),
                                  @head_receipt_hash,
                                  @head_event_hash,
                                  @checkpoint_count,
                                  @last_heartbeat_at,
                                  @lease_expires_at,
                                  @has_lease_gap,
                                  @max_lease_gap_seconds
                              );
                              """;
        command.Parameters.AddWithValue("id", sessionId.Value);
        command.Parameters.AddWithValue("created_at", createdAtUtc);
        command.Parameters.AddWithValue("metadata_json", "{}");
        command.Parameters.AddWithValue("head_receipt_hash", ReceiptChainVerifier.GenesisPreviousReceiptHash);
        command.Parameters.AddWithValue("head_event_hash", Ows.Core.Events.OwsEventChain.GenesisPreviousEventHash);
        command.Parameters.AddWithValue("checkpoint_count", 0);
        command.Parameters.AddWithValue("last_heartbeat_at", createdAtUtc);
        command.Parameters.AddWithValue("lease_expires_at", leaseExpiresAt);
        command.Parameters.AddWithValue("has_lease_gap", false);
        command.Parameters.AddWithValue("max_lease_gap_seconds", 0);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new VerifierSessionRecord
        {
            Id = sessionId,
            CreatedAtUtc = createdAtUtc,
            HeadReceiptHash = ReceiptChainVerifier.GenesisPreviousReceiptHash,
            HeadEventHash = Ows.Core.Events.OwsEventChain.GenesisPreviousEventHash,
            CheckpointCount = 0,
            LastHeartbeatAt = createdAtUtc,
            LeaseExpiresAt = leaseExpiresAt,
            HasLeaseGap = false,
            MaxLeaseGapSeconds = 0
        };
    }

    /// <inheritdoc />
    public async Task<VerifierSessionRecord> GetSessionAsync(AssessmentSessionId sessionId,
        CancellationToken cancellationToken)
    {
        await AwaitInitializationAsync(cancellationToken);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await GetRequiredSessionAsync(connection, sessionId, lockForUpdate: false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CheckpointReceipt> AppendCheckpointAsync(Checkpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        await AwaitInitializationAsync(cancellationToken);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var session =
            await GetRequiredSessionAsync(connection, checkpoint.SessionId, lockForUpdate: true, cancellationToken);
        var expectedSequenceNumber = session.CheckpointCount + 1;
        if (!string.IsNullOrWhiteSpace(checkpoint.IdempotencyKey))
        {
            var existingReceipt = await TryGetReceiptByIdempotencyKeyAsync(connection, checkpoint, _signingKey, cancellationToken);
            if (existingReceipt is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return existingReceipt;
            }
        }

        if (checkpoint.SequenceNumber <= session.CheckpointCount)
        {
            var existingReceipt = await TryGetReceiptBySequenceAsync(connection, checkpoint.SessionId,
                checkpoint.SequenceNumber, _signingKey, cancellationToken);
            if (existingReceipt is null)
            {
                throw new InvalidOperationException(
                    $"Checkpoint sequence {checkpoint.SequenceNumber} is invalid for session {checkpoint.SessionId}. Expected {expectedSequenceNumber}.");
            }

            if (!string.Equals(existingReceipt.TimelineHeadHash, checkpoint.TimelineHeadHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Checkpoint sequence {checkpoint.SequenceNumber} already exists for session {checkpoint.SessionId} with a different payload.");
            }

            await transaction.RollbackAsync(cancellationToken);
            return existingReceipt;
        }

        if (checkpoint.SequenceNumber != expectedSequenceNumber)
        {
            throw new InvalidOperationException(
                $"Checkpoint sequence {checkpoint.SequenceNumber} is invalid for session {checkpoint.SessionId}. Expected {expectedSequenceNumber}.");
        }

        var now = DateTimeOffset.UtcNow;
        var leaseDuration = TimeSpan.FromSeconds(120);

        DateTimeOffset? firstLeaseGapAt = session.FirstLeaseGapAt;
        var maxLeaseGapSeconds = session.MaxLeaseGapSeconds;
        var hasLeaseGap = session.HasLeaseGap;

        if (session.LeaseExpiresAt.HasValue && now > session.LeaseExpiresAt.Value)
        {
            hasLeaseGap = true;
            var gapSeconds = (int)(now - session.LeaseExpiresAt.Value).TotalSeconds;
            maxLeaseGapSeconds = Math.Max(maxLeaseGapSeconds, gapSeconds);
            firstLeaseGapAt ??= session.LeaseExpiresAt.Value;
        }

        var receipt = ReceiptChainVerifier.IssueReceipt(checkpoint, session.HeadReceiptHash, _signingKey);
        await InsertCheckpointAsync(connection, checkpoint, session, receipt, cancellationToken);
        await UpdateSessionHeadAsync(
            connection,
            session.Id,
            receipt,
            now,
            now.Add(leaseDuration),
            hasLeaseGap,
            maxLeaseGapSeconds,
            firstLeaseGapAt,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return receipt;
    }

    /// <inheritdoc />
    public async Task<ReceiptChain> GetReceiptsAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken)
    {
        await AwaitInitializationAsync(cancellationToken);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        _ = await GetRequiredSessionAsync(connection, sessionId, lockForUpdate: false, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select
                                  session_id,
                                  sequence_number,
                                  current_event_hash,
                                  previous_receipt_hash,
                                  receipt_hash,
                                  server_signature,
                                  server_time
                              from verifier_checkpoints
                              where session_id = @session_id
                              order by sequence_number asc;
                              """;
        command.Parameters.AddWithValue("session_id", sessionId.Value);

        var receipts = new List<CheckpointReceipt>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            receipts.Add(ReadReceipt(reader, _signingKey));
        }

        return new ReceiptChain
        {
            SessionId = sessionId,
            Receipts = receipts
        };
    }

    /// <inheritdoc />
    public async Task<SessionHeadResponse> GetHeadAsync(AssessmentSessionId sessionId,
        CancellationToken cancellationToken)
    {
        await AwaitInitializationAsync(cancellationToken);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var session = await GetRequiredSessionAsync(connection, sessionId, lockForUpdate: false, cancellationToken);
        return new SessionHeadResponse
        {
            SessionId = session.Id.Value,
            LastSequenceNumber = session.CheckpointCount,
            LastTimelineHeadHash = session.HeadEventHash,
            LastReceiptHash = session.HeadReceiptHash
        };
    }

    /// <inheritdoc />
    public async Task<VerifierSessionRecord> RecordHeartbeatAsync(
        AssessmentSessionId sessionId,
        string? lastKnownEventHash,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await AwaitInitializationAsync(cancellationToken);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var session = await GetRequiredSessionAsync(connection, sessionId, lockForUpdate: true, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        DateTimeOffset? firstLeaseGapAt = session.FirstLeaseGapAt;
        var maxLeaseGapSeconds = session.MaxLeaseGapSeconds;
        var hasLeaseGap = session.HasLeaseGap;

        if (session.LeaseExpiresAt.HasValue && now > session.LeaseExpiresAt.Value)
        {
            hasLeaseGap = true;
            var gapSeconds = (int)(now - session.LeaseExpiresAt.Value).TotalSeconds;
            maxLeaseGapSeconds = Math.Max(maxLeaseGapSeconds, gapSeconds);
            firstLeaseGapAt ??= session.LeaseExpiresAt.Value;
        }

        var updatedLeaseExpiresAt = now.Add(leaseDuration);
        var updatedEventHash = lastKnownEventHash ?? session.LastKnownEventHash;

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              update verifier_sessions
                              set last_heartbeat_at = @last_heartbeat_at,
                                  lease_expires_at = @lease_expires_at,
                                  last_known_event_hash = @last_known_event_hash,
                                  has_lease_gap = @has_lease_gap,
                                  max_lease_gap_seconds = @max_lease_gap_seconds,
                                  first_lease_gap_at = @first_lease_gap_at
                              where id = @id;
                              """;
        command.Parameters.AddWithValue("id", sessionId.Value);
        command.Parameters.AddWithValue("last_heartbeat_at", now);
        command.Parameters.AddWithValue("lease_expires_at", updatedLeaseExpiresAt);
        command.Parameters.AddWithValue("last_known_event_hash", (object?)updatedEventHash ?? DBNull.Value);
        command.Parameters.AddWithValue("has_lease_gap", hasLeaseGap);
        command.Parameters.AddWithValue("max_lease_gap_seconds", maxLeaseGapSeconds);
        command.Parameters.AddWithValue("first_lease_gap_at", (object?)firstLeaseGapAt ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return session with
        {
            LastHeartbeatAt = now,
            LeaseExpiresAt = updatedLeaseExpiresAt,
            LastKnownEventHash = updatedEventHash,
            HasLeaseGap = hasLeaseGap,
            MaxLeaseGapSeconds = maxLeaseGapSeconds,
            FirstLeaseGapAt = firstLeaseGapAt
        };
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    /// <summary>
    /// Waits for schema initialization before serving storage operations.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when initialization is ready.</returns>
    private Task AwaitInitializationAsync(CancellationToken cancellationToken) =>
        _initializationTask.WaitAsync(cancellationToken);

    /// <summary>
    /// Loads a required persisted verifier session row.
    /// </summary>
    /// <param name="connection">The open database connection.</param>
    /// <param name="sessionId">The verifier session identifier.</param>
    /// <param name="lockForUpdate">Whether to lock the session row for update.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The persisted verifier session record.</returns>
    private static async Task<VerifierSessionRecord> GetRequiredSessionAsync(
        NpgsqlConnection connection,
        AssessmentSessionId sessionId,
        bool lockForUpdate,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               select
                                   id,
                                   created_at,
                                   client_id,
                                   assessment_id,
                                   metadata_json::text,
                                   head_receipt_hash,
                                   head_event_hash,
                                   checkpoint_count,
                                   last_heartbeat_at,
                                   lease_expires_at,
                                   last_known_event_hash,
                                   has_lease_gap,
                                   max_lease_gap_seconds,
                                   first_lease_gap_at
                               from verifier_sessions
                               where id = @id
                               {(lockForUpdate ? "for update" : string.Empty)};
                               """;
        command.Parameters.AddWithValue("id", sessionId.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Unknown assessment session: {sessionId}");
        }

        var session = new VerifierSessionRecord
        {
            Id = new AssessmentSessionId(reader.GetString(0)),
            CreatedAtUtc = reader.GetFieldValue<DateTimeOffset>(1),
            ClientId = reader.IsDBNull(2) ? null : reader.GetString(2),
            AssessmentId = reader.IsDBNull(3) ? null : reader.GetString(3),
            MetadataJson = reader.GetString(4),
            HeadReceiptHash = reader.GetString(5),
            HeadEventHash = reader.GetString(6),
            CheckpointCount = reader.GetInt32(7),
            LastHeartbeatAt = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            LeaseExpiresAt = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            LastKnownEventHash = reader.IsDBNull(10) ? null : reader.GetString(10),
            HasLeaseGap = reader.GetBoolean(11),
            MaxLeaseGapSeconds = reader.GetInt32(12),
            FirstLeaseGapAt = reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13)
        };
        await reader.CloseAsync();
        return session;
    }

    /// <summary>
    /// Reads an existing receipt by session and sequence number for retry handling.
    /// </summary>
    /// <param name="connection">The open database connection.</param>
    /// <param name="sessionId">The verifier session identifier.</param>
    /// <param name="sequenceNumber">The requested sequence number.</param>
    /// <param name="signingKey">The optional server signing key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The existing committed receipt when found; otherwise <see langword="null"/>.</returns>
    private static async Task<CheckpointReceipt?> TryGetReceiptBySequenceAsync(
        NpgsqlConnection connection,
        AssessmentSessionId sessionId,
        int sequenceNumber,
        string? signingKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select
                                  session_id,
                                  sequence_number,
                                  current_event_hash,
                                  previous_receipt_hash,
                                  receipt_hash,
                                  server_signature,
                                  server_time
                              from verifier_checkpoints
                              where session_id = @session_id and sequence_number = @sequence_number;
                              """;
        command.Parameters.AddWithValue("session_id", sessionId.Value);
        command.Parameters.AddWithValue("sequence_number", sequenceNumber);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var receipt = ReadReceipt(reader, signingKey);
        await reader.CloseAsync();
        return receipt;
    }

    /// <summary>
    /// Inserts a committed checkpoint row.
    /// </summary>
    /// <param name="connection">The open database connection.</param>
    /// <param name="checkpoint">The checkpoint being appended.</param>
    /// <param name="session">The locked verifier session record.</param>
    /// <param name="receipt">The issued receipt.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async Task InsertCheckpointAsync(
        NpgsqlConnection connection,
        Checkpoint checkpoint,
        VerifierSessionRecord session,
        CheckpointReceipt receipt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              insert into verifier_checkpoints (
                                  session_id,
                                  sequence_number,
                                  client_time,
                                  server_time,
                                  previous_event_hash,
                                  current_event_hash,
                                  project_state_hash,
                                  previous_receipt_hash,
                                  receipt_hash,
                                  server_signature,
                                  idempotency_key
                              )
                              values (
                                  @session_id,
                                  @sequence_number,
                                  @client_time,
                                  @server_time,
                                  @previous_event_hash,
                                  @current_event_hash,
                                  @project_state_hash,
                                  @previous_receipt_hash,
                                  @receipt_hash,
                                  @server_signature,
                                  @idempotency_key
                              );
                              """;
        command.Parameters.AddWithValue("session_id", checkpoint.SessionId.Value);
        command.Parameters.AddWithValue("sequence_number", checkpoint.SequenceNumber);
        command.Parameters.AddWithValue("client_time", checkpoint.CreatedAtUtc);
        command.Parameters.AddWithValue("server_time", receipt.ServerTimestamp.IssuedAtUtc);
        command.Parameters.AddWithValue("previous_event_hash", session.HeadEventHash);
        command.Parameters.AddWithValue("current_event_hash", checkpoint.TimelineHeadHash);
        command.Parameters.AddWithValue("project_state_hash", checkpoint.TimelineHeadHash);
        command.Parameters.AddWithValue("previous_receipt_hash", receipt.PreviousReceiptHash);
        command.Parameters.AddWithValue("receipt_hash", receipt.ReceiptHash);
        command.Parameters.AddWithValue("server_signature", receipt.ServerSignature);
        command.Parameters.AddWithValue("idempotency_key", (object?)checkpoint.IdempotencyKey ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Reads an existing receipt by session and idempotency key for retry handling.
    /// </summary>
    /// <param name="connection">The open database connection.</param>
    /// <param name="checkpoint">The requested checkpoint.</param>
    /// <param name="signingKey">The optional server signing key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The existing committed receipt when found; otherwise <see langword="null"/>.</returns>
    private static async Task<CheckpointReceipt?> TryGetReceiptByIdempotencyKeyAsync(
        NpgsqlConnection connection,
        Checkpoint checkpoint,
        string? signingKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              select
                                  session_id,
                                  sequence_number,
                                  current_event_hash,
                                  previous_receipt_hash,
                                  receipt_hash,
                                  server_signature,
                                  server_time
                              from verifier_checkpoints
                              where session_id = @session_id and idempotency_key = @idempotency_key;
                              """;
        command.Parameters.AddWithValue("session_id", checkpoint.SessionId.Value);
        command.Parameters.AddWithValue("idempotency_key", checkpoint.IdempotencyKey!);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var receipt = ReadReceipt(reader, signingKey);
        await reader.CloseAsync();
        if (receipt.SequenceNumber != checkpoint.SequenceNumber ||
            !string.Equals(receipt.TimelineHeadHash, checkpoint.TimelineHeadHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Idempotency key {checkpoint.IdempotencyKey} already exists for session {checkpoint.SessionId} with a different payload.");
        }

        return receipt;
    }

    /// <summary>
    /// Updates the durable verifier session head after a committed checkpoint append.
    /// </summary>
    /// <param name="connection">The open database connection.</param>
    /// <param name="sessionId">The verifier session identifier.</param>
    /// <param name="receipt">The committed receipt.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async Task UpdateSessionHeadAsync(
        NpgsqlConnection connection,
        AssessmentSessionId sessionId,
        CheckpointReceipt receipt,
        DateTimeOffset lastHeartbeatAt,
        DateTimeOffset leaseExpiresAt,
        bool hasLeaseGap,
        int maxLeaseGapSeconds,
        DateTimeOffset? firstLeaseGapAt,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
                              update verifier_sessions
                              set head_receipt_hash = @head_receipt_hash,
                                  head_event_hash = @head_event_hash,
                                  checkpoint_count = @checkpoint_count,
                                  last_heartbeat_at = @last_heartbeat_at,
                                  lease_expires_at = @lease_expires_at,
                                  has_lease_gap = @has_lease_gap,
                                  max_lease_gap_seconds = @max_lease_gap_seconds,
                                  first_lease_gap_at = @first_lease_gap_at
                              where id = @id;
                              """;
        command.Parameters.AddWithValue("id", sessionId.Value);
        command.Parameters.AddWithValue("head_receipt_hash", receipt.ReceiptHash);
        command.Parameters.AddWithValue("head_event_hash", receipt.TimelineHeadHash);
        command.Parameters.AddWithValue("checkpoint_count", receipt.SequenceNumber);
        command.Parameters.AddWithValue("last_heartbeat_at", lastHeartbeatAt);
        command.Parameters.AddWithValue("lease_expires_at", leaseExpiresAt);
        command.Parameters.AddWithValue("has_lease_gap", hasLeaseGap);
        command.Parameters.AddWithValue("max_lease_gap_seconds", maxLeaseGapSeconds);
        command.Parameters.AddWithValue("first_lease_gap_at", (object?)firstLeaseGapAt ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Maps a checkpoint row reader to the public receipt model.
    /// </summary>
    /// <param name="reader">The active row reader.</param>
    /// <param name="signingKey">The optional server signing key.</param>
    /// <returns>The mapped checkpoint receipt.</returns>
    private static CheckpointReceipt ReadReceipt(NpgsqlDataReader reader, string? signingKey) =>
        new()
        {
            SessionId = new AssessmentSessionId(reader.GetString(0)),
            SequenceNumber = reader.GetInt32(1),
            TimelineHeadHash = reader.GetString(2),
            PreviousReceiptHash = reader.GetString(3),
            ReceiptHash = reader.GetString(4),
            ServerSignature = reader.GetString(5),
            ServerTimestamp = new ServerTimestamp
            {
                IssuedAtUtc = reader.GetFieldValue<DateTimeOffset>(6)
            },
            SigningKeyFingerprint = ReceiptChainVerifier.ComputeFingerprint(signingKey)
        };
}
