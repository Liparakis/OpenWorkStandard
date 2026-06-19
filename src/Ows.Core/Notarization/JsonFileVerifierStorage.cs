using System.Text.Json;

namespace Ows.Core.Notarization;

/// <summary>
/// Persists verifier sessions and receipt chains to a local JSON snapshot file for development use.
/// </summary>
public sealed class JsonFileVerifierStorage : IVerifierStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly Lock _gate = new();

    private readonly Dictionary<AssessmentSessionId, Dictionary<string, PersistedCheckpointRequest>> _idempotencyKeys =
        [];

    private readonly InMemoryReceiptService _receiptService;
    private readonly Dictionary<AssessmentSessionId, VerifierSessionRecord> _sessions = [];
    private readonly string _storePath;

    /// <summary>
    /// Initializes a new file-backed verifier storage instance and restores any existing sessions.
    /// </summary>
    /// <param name="storePath">The snapshot file path.</param>
    /// <param name="signingKey">The optional server signing key used to sign issued receipts.</param>
    public JsonFileVerifierStorage(string storePath, string? signingKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);

        this._storePath = storePath;
        _receiptService = new InMemoryReceiptService(signingKey);
        LoadFromDisk();
    }

    /// <inheritdoc />
    public Task<VerifierSessionRecord> CreateSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var sessionId = _receiptService.StartSession();
            var sessionRecord = new VerifierSessionRecord
            {
                Id = sessionId,
                HeadReceiptHash = ReceiptChainVerifier.GenesisPreviousReceiptHash,
                HeadEventHash = Events.OwsEventChain.GenesisPreviousEventHash
            };
            _sessions.Add(sessionId, sessionRecord);
            SaveToDisk();
            return Task.FromResult(sessionRecord);
        }
    }

    /// <inheritdoc />
    public Task<VerifierSessionRecord> GetSessionAsync(AssessmentSessionId sessionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(GetRequiredSession(sessionId));
        }
    }

    /// <inheritdoc />
    public Task<CheckpointReceipt> AppendCheckpointAsync(Checkpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var session = GetRequiredSession(checkpoint.SessionId);
            if (!string.IsNullOrWhiteSpace(checkpoint.IdempotencyKey))
            {
                var existingReceipt = TryGetReceiptByIdempotencyKey(session.Id, checkpoint);
                if (existingReceipt is not null)
                {
                    return Task.FromResult(existingReceipt);
                }
            }

            if (checkpoint.SequenceNumber <= session.CheckpointCount)
            {
                var existingReceipt = _receiptService.GetReceiptChain(checkpoint.SessionId)
                    .Receipts
                    .SingleOrDefault(receipt => receipt.SequenceNumber == checkpoint.SequenceNumber);
                if (existingReceipt is null)
                {
                    throw new InvalidOperationException(
                        $"Checkpoint sequence {checkpoint.SequenceNumber} is invalid for session {checkpoint.SessionId}. Expected {session.CheckpointCount + 1}.");
                }

                if (!string.Equals(existingReceipt.TimelineHeadHash, checkpoint.TimelineHeadHash,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Checkpoint sequence {checkpoint.SequenceNumber} already exists for session {checkpoint.SessionId} with a different payload.");
                }

                return Task.FromResult(existingReceipt);
            }

            var receipt = _receiptService.SubmitCheckpoint(checkpoint);
            _sessions[checkpoint.SessionId] = _sessions[checkpoint.SessionId] with
            {
                HeadReceiptHash = receipt.ReceiptHash,
                HeadEventHash = receipt.TimelineHeadHash,
                CheckpointCount = receipt.SequenceNumber
            };
            RememberIdempotencyKey(checkpoint);
            SaveToDisk();
            return Task.FromResult(receipt);
        }
    }

    /// <inheritdoc />
    public Task<ReceiptChain> GetReceiptsAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _ = GetRequiredSession(sessionId);
            return Task.FromResult(_receiptService.GetReceiptChain(sessionId));
        }
    }

    /// <inheritdoc />
    public Task<SessionHeadResponse> GetHeadAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var session = GetRequiredSession(sessionId);
            return Task.FromResult(new SessionHeadResponse
            {
                SessionId = session.Id.Value,
                LastSequenceNumber = session.CheckpointCount,
                LastTimelineHeadHash = session.HeadEventHash,
                LastReceiptHash = session.HeadReceiptHash
            });
        }
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> CheckHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var dir = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Restores existing session snapshots from disk when the server starts.
    /// </summary>
    private void LoadFromDisk()
    {
        if (!File.Exists(_storePath))
        {
            return;
        }

        var json = File.ReadAllText(_storePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var snapshot = JsonSerializer.Deserialize<VerifierStorageSnapshot>(json, SerializerOptions)
                       ?? throw new InvalidOperationException(
                           $"Verifier store {_storePath} could not be deserialized.");

        foreach (var session in snapshot.Sessions)
        {
            _sessions.Add(session.Id, session);
        }

        foreach (var receiptChain in snapshot.ReceiptChains)
        {
            _receiptService.RestoreSession(receiptChain.SessionId, receiptChain.Receipts);
        }

        foreach (var request in snapshot.IdempotencyKeys)
        {
            if (!_idempotencyKeys.TryGetValue(request.SessionId, out var sessionKeys))
            {
                sessionKeys = [];
                _idempotencyKeys.Add(request.SessionId, sessionKeys);
            }

            sessionKeys[request.IdempotencyKey] = request;
        }
    }

    /// <summary>
    /// Writes the complete verifier snapshot to disk after each mutating operation.
    /// </summary>
    private void SaveToDisk()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);

        // ponytail: rewrite the full snapshot on each change; switch to database transactions when concurrent multi-instance writes become the real problem.
        var json = JsonSerializer.Serialize(
            new VerifierStorageSnapshot
            {
                Sessions = [.. _sessions.Values.OrderBy(session => session.Id.Value, StringComparer.Ordinal)],
                ReceiptChains =
                [
                    .. _sessions.Keys.OrderBy(sessionId => sessionId.Value, StringComparer.Ordinal)
                        .Select(_receiptService.GetReceiptChain)
                ],
                IdempotencyKeys =
                [
                    .. _idempotencyKeys
                        .OrderBy(entry => entry.Key.Value, StringComparer.Ordinal)
                        .SelectMany(entry =>
                            entry.Value.Values.OrderBy(request => request.IdempotencyKey, StringComparer.Ordinal))
                ]
            },
            SerializerOptions);
        var temporaryPath = $"{_storePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _storePath, true);
    }

    /// <summary>
    /// Gets a required persisted verifier session or throws when it does not exist.
    /// </summary>
    /// <param name="sessionId">The verifier session identifier.</param>
    /// <returns>The persisted verifier session record.</returns>
    private VerifierSessionRecord GetRequiredSession(AssessmentSessionId sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Unknown assessment session: {sessionId}");
        }

        return session;
    }

    /// <summary>
    /// Returns a committed receipt for a repeated idempotent request or rejects payload drift.
    /// </summary>
    /// <param name="sessionId">The verifier session identifier.</param>
    /// <param name="checkpoint">The requested checkpoint.</param>
    /// <returns>The committed receipt when the idempotency key already exists; otherwise <see langword="null"/>.</returns>
    private CheckpointReceipt? TryGetReceiptByIdempotencyKey(AssessmentSessionId sessionId, Checkpoint checkpoint)
    {
        if (!_idempotencyKeys.TryGetValue(sessionId, out var sessionKeys) ||
            !sessionKeys.TryGetValue(checkpoint.IdempotencyKey!, out var persistedRequest))
        {
            return null;
        }

        if (persistedRequest.SequenceNumber != checkpoint.SequenceNumber ||
            !string.Equals(persistedRequest.TimelineHeadHash, checkpoint.TimelineHeadHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Idempotency key {checkpoint.IdempotencyKey} already exists for session {sessionId} with a different payload.");
        }

        return _receiptService.GetReceiptChain(sessionId)
            .Receipts
            .Single(receipt => receipt.SequenceNumber == persistedRequest.SequenceNumber);
    }

    /// <summary>
    /// Persists the idempotency key for a committed checkpoint request.
    /// </summary>
    /// <param name="checkpoint">The committed checkpoint.</param>
    private void RememberIdempotencyKey(Checkpoint checkpoint)
    {
        if (string.IsNullOrWhiteSpace(checkpoint.IdempotencyKey))
        {
            return;
        }

        if (!_idempotencyKeys.TryGetValue(checkpoint.SessionId, out var sessionKeys))
        {
            sessionKeys = [];
            _idempotencyKeys.Add(checkpoint.SessionId, sessionKeys);
        }

        sessionKeys[checkpoint.IdempotencyKey] = new PersistedCheckpointRequest
        {
            SessionId = checkpoint.SessionId,
            IdempotencyKey = checkpoint.IdempotencyKey,
            SequenceNumber = checkpoint.SequenceNumber,
            TimelineHeadHash = checkpoint.TimelineHeadHash
        };
    }

    private sealed record VerifierStorageSnapshot
    {
        public IReadOnlyList<VerifierSessionRecord> Sessions { get; init; } = Array.Empty<VerifierSessionRecord>();

        public IReadOnlyList<ReceiptChain> ReceiptChains { get; init; } = Array.Empty<ReceiptChain>();

        public IReadOnlyList<PersistedCheckpointRequest> IdempotencyKeys { get; init; } =
            Array.Empty<PersistedCheckpointRequest>();
    }

    private sealed record PersistedCheckpointRequest
    {
        public AssessmentSessionId SessionId { get; init; } = AssessmentSessionId.Create();

        public string IdempotencyKey { get; init; } = string.Empty;

        public int SequenceNumber { get; init; }

        public string TimelineHeadHash { get; init; } = string.Empty;
    }
}
