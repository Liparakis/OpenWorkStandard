using System.Text.Json;

using Ows.Core;
using Ows.Core.Notarization;

namespace Ows.Cli;

/// <summary>
/// Persists the minimal local session and receipt state used by the CLI prototype flow.
/// </summary>
public static class OwsSessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>
    /// Starts a new session and persists its identifier and transport details.
    /// </summary>
    /// <param name="projectRoot">The current project root.</param>
    /// <param name="verifierUrl">The optional remote verifier base URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The started session identifier.</returns>
    public static async Task<AssessmentSessionId> StartSessionAsync(
        string projectRoot,
        string? verifierUrl,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        cancellationToken.ThrowIfCancellationRequested();

        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        Directory.CreateDirectory(localFolder);
        var sessionId = string.IsNullOrWhiteSpace(verifierUrl)
            ? new InMemoryReceiptService().StartSession()
            : await StartRemoteSessionAsync(verifierUrl, cancellationToken);

        WriteSessionState(
            Path.Combine(localFolder, SessionFileName),
            new SessionState { SessionId = sessionId.Value, VerifierUrl = verifierUrl });
        WriteReceiptChain(
            Path.Combine(localFolder, OwsConstants.ReceiptsFileName),
            new ReceiptChain { SessionId = sessionId, Receipts = [] });

        return sessionId;
    }

    /// <summary>
    /// Gets the packaged session-state file path.
    /// </summary>
    private static string SessionFileName => OwsConstants.SessionFileName;

    /// <summary>
    /// Derives the next checkpoint from the local timeline head, issues a receipt, and persists the updated chain.
    /// </summary>
    /// <param name="projectRoot">The current project root.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The issued receipt.</returns>
    public static async Task<CheckpointReceipt> AddCheckpointAsync(string projectRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        cancellationToken.ThrowIfCancellationRequested();

        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, SessionFileName);
        var receiptsPath = Path.Combine(localFolder, OwsConstants.ReceiptsFileName);
        var timelinePath = Path.Combine(localFolder, OwsConstants.TimelineFileName);

        if (!File.Exists(sessionPath))
        {
            throw new InvalidOperationException("No active OWS session. Run 'ows session start' first.");
        }

        var sessionState = ReadSessionState(sessionPath);
        var sessionId = new AssessmentSessionId(sessionState.SessionId);
        var receiptChain = File.Exists(receiptsPath)
            ? JsonSerializer.Deserialize<ReceiptChain>(File.ReadAllText(receiptsPath))
                ?? throw new JsonException("Receipt chain deserialized to null.")
            : new ReceiptChain { SessionId = sessionId, Receipts = [] };

        CheckpointReceipt receipt;
        ReceiptChain updatedReceiptChain;

        if (string.IsNullOrWhiteSpace(sessionState.VerifierUrl))
        {
            var checkpoint = Checkpoint.FromTimeline(timelinePath, sessionId, receiptChain.Receipts.Count + 1);
            var service = new InMemoryReceiptService();
            service.RestoreSession(sessionId, receiptChain.Receipts);
            receipt = service.SubmitCheckpoint(checkpoint);
            updatedReceiptChain = service.GetReceiptChain(sessionId);
        }
        else
        {
            using var httpClient = CreateHttpClient(sessionState.VerifierUrl);
            var transport = new HttpsReceiptTransport(
                httpClient,
                (activeSessionId, sequenceNumber) => Checkpoint.FromTimeline(timelinePath, activeSessionId, sequenceNumber));
            transport.RestoreSession(sessionId, receiptChain.Receipts.Count + 1);
            receipt = await transport.SendCheckpointAsync(cancellationToken);
            updatedReceiptChain = await transport.GetReceiptsAsync(cancellationToken);
        }

        WriteReceiptChain(receiptsPath, updatedReceiptChain);

        return receipt;
    }
    /// <summary>
    /// Reads the persisted receipt chain for the current project.
    /// </summary>
    /// <param name="projectRoot">The current project root.</param>
    /// <returns>The current persisted receipt chain.</returns>
    public static ReceiptChain GetReceipts(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
        var sessionPath = Path.Combine(localFolder, SessionFileName);
        var receiptsPath = Path.Combine(localFolder, OwsConstants.ReceiptsFileName);

        if (!File.Exists(sessionPath))
        {
            throw new InvalidOperationException("No active OWS session. Run 'ows session start' first.");
        }

        var sessionState = ReadSessionState(sessionPath);
        var sessionId = new AssessmentSessionId(sessionState.SessionId);

        if (!File.Exists(receiptsPath))
        {
            return new ReceiptChain
            {
                SessionId = sessionId,
                Receipts = []
            };
        }

        return JsonSerializer.Deserialize<ReceiptChain>(File.ReadAllText(receiptsPath))
            ?? throw new JsonException("Receipt chain deserialized to null.");
    }

    /// <summary>
    /// Starts a remote session against the configured verifier API.
    /// </summary>
    /// <param name="verifierUrl">The remote verifier base URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The started remote session identifier.</returns>
    private static async Task<AssessmentSessionId> StartRemoteSessionAsync(string verifierUrl, CancellationToken cancellationToken)
    {
        using var httpClient = CreateHttpClient(verifierUrl);
        var transport = new HttpsReceiptTransport(httpClient, (_, _) => new Checkpoint());
        return await transport.StartSessionAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the HTTP client used for remote verifier calls.
    /// </summary>
    /// <param name="verifierUrl">The remote verifier base URL.</param>
    /// <returns>The configured HTTP client.</returns>
    private static HttpClient CreateHttpClient(string verifierUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifierUrl);
        return new HttpClient { BaseAddress = new Uri(verifierUrl, UriKind.Absolute) };
    }

    /// <summary>
    /// Reads the persisted session state for the current project.
    /// </summary>
    /// <param name="sessionPath">The persisted session-state path.</param>
    /// <returns>The deserialized session state.</returns>
    private static SessionState ReadSessionState(string sessionPath) =>
        JsonSerializer.Deserialize<SessionState>(File.ReadAllText(sessionPath))
        ?? throw new JsonException("Session state deserialized to null.");

    /// <summary>
    /// Writes the current session state to disk.
    /// </summary>
    /// <param name="sessionPath">The persisted session-state path.</param>
    /// <param name="sessionState">The session state to persist.</param>
    private static void WriteSessionState(string sessionPath, SessionState sessionState) =>
        File.WriteAllText(sessionPath, JsonSerializer.Serialize(sessionState, SerializerOptions));

    /// <summary>
    /// Writes the current receipt chain snapshot to disk.
    /// </summary>
    /// <param name="receiptsPath">The receipt-chain path.</param>
    /// <param name="receiptChain">The receipt chain to persist.</param>
    private static void WriteReceiptChain(string receiptsPath, ReceiptChain receiptChain) =>
        File.WriteAllText(receiptsPath, JsonSerializer.Serialize(receiptChain, SerializerOptions));
}
