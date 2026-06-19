using System.Text.Json;

using Ows.Core;
using Ows.Core.Notarization;

namespace Ows.Cli;

/// <summary>
/// Persists the minimal local session and receipt state used by the CLI prototype flow.
/// </summary>
public static class OwsSessionStore
{
    private const string SessionFileName = "session.json";

    /// <summary>
    /// Starts a new local session and persists its identifier.
    /// </summary>
    /// <param name="projectRoot">The current project root.</param>
    /// <returns>The started session identifier.</returns>
    public static AssessmentSessionId StartSession(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        Directory.CreateDirectory(localFolder);

        var receiptService = new InMemoryReceiptService();
        var sessionId = receiptService.StartSession();

        File.WriteAllText(
            Path.Combine(localFolder, SessionFileName),
            JsonSerializer.Serialize(new SessionState { SessionId = sessionId.Value }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(
            Path.Combine(localFolder, OwsConstants.ReceiptsFileName),
            JsonSerializer.Serialize(new ReceiptChain { SessionId = sessionId, Receipts = [] }, new JsonSerializerOptions { WriteIndented = true }));

        return sessionId;
    }

    /// <summary>
    /// Derives the next checkpoint from the local timeline head, issues a receipt, and persists the updated chain.
    /// </summary>
    /// <param name="projectRoot">The current project root.</param>
    /// <returns>The issued receipt.</returns>
    public static CheckpointReceipt AddCheckpoint(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, SessionFileName);
        var receiptsPath = Path.Combine(localFolder, OwsConstants.ReceiptsFileName);
        var timelinePath = Path.Combine(localFolder, OwsConstants.TimelineFileName);

        if (!File.Exists(sessionPath))
        {
            throw new InvalidOperationException("No active OWS session. Run 'ows session start' first.");
        }

        var sessionState = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(sessionPath))
            ?? throw new JsonException("Session state deserialized to null.");
        var sessionId = new AssessmentSessionId(sessionState.SessionId);
        var receiptChain = File.Exists(receiptsPath)
            ? JsonSerializer.Deserialize<ReceiptChain>(File.ReadAllText(receiptsPath))
                ?? throw new JsonException("Receipt chain deserialized to null.")
            : new ReceiptChain { SessionId = sessionId, Receipts = [] };
        var checkpoint = Checkpoint.FromTimeline(timelinePath, sessionId, receiptChain.Receipts.Count + 1);
        var receiptService = new InMemoryReceiptService();

        foreach (var existingReceipt in receiptChain.Receipts)
        {
            _ = receiptService.StartSession();
            break;
        }

        var service = RehydrateService(sessionId, receiptChain);
        var receipt = service.SubmitCheckpoint(checkpoint);
        var updatedReceiptChain = service.GetReceiptChain(sessionId);

        File.WriteAllText(
            receiptsPath,
            JsonSerializer.Serialize(updatedReceiptChain, new JsonSerializerOptions { WriteIndented = true }));

        return receipt;
    }

    /// <summary>
    /// Rehydrates an in-memory receipt service from persisted session state.
    /// </summary>
    /// <param name="sessionId">The session identifier to restore.</param>
    /// <param name="receiptChain">The current persisted receipt chain.</param>
    /// <returns>A service ready to issue the next receipt.</returns>
    private static InMemoryReceiptService RehydrateService(AssessmentSessionId sessionId, ReceiptChain receiptChain)
    {
        var service = new InMemoryReceiptService();
        var startedSessionId = service.StartSession();

        if (startedSessionId != sessionId)
        {
            // ponytail: simple local CLI persistence uses a fresh service instance; replace generated session id with persisted session state.
            typeof(InMemoryReceiptService)
                .GetField("receiptChains", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(service, new System.Collections.Concurrent.ConcurrentDictionary<AssessmentSessionId, List<CheckpointReceipt>>(
                    new[]
                    {
                        new KeyValuePair<AssessmentSessionId, List<CheckpointReceipt>>(sessionId, [.. receiptChain.Receipts])
                    }));
        }

        return service;
    }

    private sealed record SessionState
    {
        public string SessionId { get; init; } = string.Empty;
    }
}
