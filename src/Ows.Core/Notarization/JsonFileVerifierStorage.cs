using System.Text.Json;

namespace Ows.Core.Notarization;

/// <summary>
/// Persists verifier sessions and receipt chains to a local JSON snapshot file for development use.
/// </summary>
public sealed class JsonFileVerifierStorage : IVerifierStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly object gate = new();
    private readonly InMemoryReceiptService receiptService = new();
    private readonly Dictionary<AssessmentSessionId, VerifierSessionRecord> sessions = [];
    private readonly string storePath;

    /// <summary>
    /// Initializes a new file-backed verifier storage instance and restores any existing sessions.
    /// </summary>
    /// <param name="storePath">The snapshot file path.</param>
    public JsonFileVerifierStorage(string storePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);

        this.storePath = storePath;
        LoadFromDisk();
    }

    /// <inheritdoc />
    public Task<VerifierSessionRecord> CreateSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            var sessionId = receiptService.StartSession();
            var sessionRecord = new VerifierSessionRecord
            {
                Id = sessionId,
                HeadReceiptHash = ReceiptChainVerifier.GenesisPreviousReceiptHash,
                HeadEventHash = Ows.Core.Events.OwsEventChain.GenesisPreviousEventHash
            };
            sessions.Add(sessionId, sessionRecord);
            SaveToDisk();
            return Task.FromResult(sessionRecord);
        }
    }

    /// <inheritdoc />
    public Task<VerifierSessionRecord> GetSessionAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            return Task.FromResult(GetRequiredSession(sessionId));
        }
    }

    /// <inheritdoc />
    public Task<CheckpointReceipt> AppendCheckpointAsync(Checkpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            var session = GetRequiredSession(checkpoint.SessionId);
            if (checkpoint.SequenceNumber <= session.CheckpointCount)
            {
                var existingReceipt = receiptService.GetReceiptChain(checkpoint.SessionId)
                    .Receipts
                    .SingleOrDefault(receipt => receipt.SequenceNumber == checkpoint.SequenceNumber);
                if (existingReceipt is null)
                {
                    throw new InvalidOperationException(
                        $"Checkpoint sequence {checkpoint.SequenceNumber} is invalid for session {checkpoint.SessionId}. Expected {session.CheckpointCount + 1}.");
                }

                if (!string.Equals(existingReceipt.TimelineHeadHash, checkpoint.TimelineHeadHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Checkpoint sequence {checkpoint.SequenceNumber} already exists for session {checkpoint.SessionId} with a different payload.");
                }

                return Task.FromResult(existingReceipt);
            }

            var receipt = receiptService.SubmitCheckpoint(checkpoint);
            sessions[checkpoint.SessionId] = sessions[checkpoint.SessionId] with
            {
                HeadReceiptHash = receipt.ReceiptHash,
                HeadEventHash = receipt.TimelineHeadHash,
                CheckpointCount = receipt.SequenceNumber
            };
            SaveToDisk();
            return Task.FromResult(receipt);
        }
    }

    /// <inheritdoc />
    public Task<ReceiptChain> GetReceiptsAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            _ = GetRequiredSession(sessionId);
            return Task.FromResult(receiptService.GetReceiptChain(sessionId));
        }
    }

    /// <inheritdoc />
    public Task<SessionHeadResponse> GetHeadAsync(AssessmentSessionId sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
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

    /// <summary>
    /// Restores existing session snapshots from disk when the server starts.
    /// </summary>
    private void LoadFromDisk()
    {
        if (!File.Exists(storePath))
        {
            return;
        }

        var json = File.ReadAllText(storePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var snapshot = JsonSerializer.Deserialize<VerifierStorageSnapshot>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Verifier store {storePath} could not be deserialized.");

        foreach (var session in snapshot.Sessions)
        {
            sessions.Add(session.Id, session);
        }

        foreach (var receiptChain in snapshot.ReceiptChains)
        {
            receiptService.RestoreSession(receiptChain.SessionId, receiptChain.Receipts);
        }
    }

    /// <summary>
    /// Writes the complete verifier snapshot to disk after each mutating operation.
    /// </summary>
    private void SaveToDisk()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);

        // ponytail: rewrite the full snapshot on each change; switch to database transactions when concurrent multi-instance writes become the real problem.
        var json = JsonSerializer.Serialize(
            new VerifierStorageSnapshot
            {
                Sessions = [.. sessions.Values.OrderBy(session => session.Id.Value, StringComparer.Ordinal)],
                ReceiptChains = [.. sessions.Keys.OrderBy(sessionId => sessionId.Value, StringComparer.Ordinal).Select(receiptService.GetReceiptChain)]
            },
            SerializerOptions);
        var temporaryPath = $"{storePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, storePath, true);
    }

    /// <summary>
    /// Gets a required persisted verifier session or throws when it does not exist.
    /// </summary>
    /// <param name="sessionId">The verifier session identifier.</param>
    /// <returns>The persisted verifier session record.</returns>
    private VerifierSessionRecord GetRequiredSession(AssessmentSessionId sessionId)
    {
        if (!sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException($"Unknown assessment session: {sessionId}");
        }

        return session;
    }

    private sealed record VerifierStorageSnapshot
    {
        public IReadOnlyList<VerifierSessionRecord> Sessions { get; init; } = Array.Empty<VerifierSessionRecord>();

        public IReadOnlyList<ReceiptChain> ReceiptChains { get; init; } = Array.Empty<ReceiptChain>();
    }
}
