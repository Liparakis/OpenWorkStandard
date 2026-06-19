using System.Text.Json;

namespace Ows.Core.Notarization;

/// <summary>
/// Persists receipt sessions to a local JSON snapshot file.
/// </summary>
public sealed class JsonFileReceiptService
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly object gate = new();
    private readonly InMemoryReceiptService receiptService = new();
    private readonly HashSet<AssessmentSessionId> sessionIds = [];
    private readonly string storePath;

    /// <summary>
    /// Initializes a new file-backed receipt service and restores any existing sessions.
    /// </summary>
    /// <param name="storePath">The snapshot file path.</param>
    public JsonFileReceiptService(string storePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storePath);

        this.storePath = storePath;
        LoadFromDisk();
    }

    /// <summary>
    /// Starts a new assessment session and persists it immediately.
    /// </summary>
    /// <returns>The new session identifier.</returns>
    public AssessmentSessionId StartSession()
    {
        lock (gate)
        {
            var sessionId = receiptService.StartSession();
            sessionIds.Add(sessionId);
            SaveToDisk();
            return sessionId;
        }
    }

    /// <summary>
    /// Submits a checkpoint, issues a receipt, and persists the updated session chain.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to receipt.</param>
    /// <returns>The issued receipt.</returns>
    public CheckpointReceipt SubmitCheckpoint(Checkpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        lock (gate)
        {
            var receipt = receiptService.SubmitCheckpoint(checkpoint);
            sessionIds.Add(checkpoint.SessionId);
            SaveToDisk();
            return receipt;
        }
    }

    /// <summary>
    /// Gets the current receipt chain for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>The current ordered receipt chain.</returns>
    public ReceiptChain GetReceiptChain(AssessmentSessionId sessionId)
    {
        lock (gate)
        {
            return receiptService.GetReceiptChain(sessionId);
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

        var persistedChains = JsonSerializer.Deserialize<List<ReceiptChain>>(json, SerializerOptions)
            ?? throw new InvalidOperationException($"Receipt store {storePath} could not be deserialized.");

        foreach (var receiptChain in persistedChains)
        {
            receiptService.RestoreSession(receiptChain.SessionId, receiptChain.Receipts);
            sessionIds.Add(receiptChain.SessionId);
        }
    }

    /// <summary>
    /// Writes the complete session snapshot to disk after each mutating operation.
    /// </summary>
    private void SaveToDisk()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);

        // ponytail: rewrite the full snapshot on each change; switch to append-only or database storage only when measured write volume makes this hurt.
        var json = JsonSerializer.Serialize(GetAllReceiptChains(), SerializerOptions);
        var temporaryPath = $"{storePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, storePath, true);
    }

    /// <summary>
    /// Materializes the current in-memory session snapshot for persistence.
    /// </summary>
    /// <returns>The ordered receipt chains known to the service.</returns>
    private List<ReceiptChain> GetAllReceiptChains() =>
        [.. sessionIds
            .OrderBy(sessionId => sessionId.Value, StringComparer.Ordinal)
            .Select(receiptService.GetReceiptChain)];
}
