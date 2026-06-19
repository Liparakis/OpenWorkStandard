using System.Net.Http.Json;

namespace Ows.Core.Notarization;

/// <summary>
/// Implements the receipt transport contract over HTTPS using the planned verifier API shape.
/// </summary>
public sealed class HttpsReceiptTransport(HttpClient httpClient, Func<AssessmentSessionId, int, Checkpoint> checkpointFactory) : IReceiptTransport
{
    private AssessmentSessionId? activeSessionId;
    private int nextSequenceNumber = 1;

    /// <summary>
    /// Restores an already-started session so the transport can continue from persisted state.
    /// </summary>
    /// <param name="sessionId">The active session identifier.</param>
    /// <param name="nextSequenceNumber">The next sequence number expected by the verifier.</param>
    public void RestoreSession(AssessmentSessionId sessionId, int nextSequenceNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId.Value);

        if (nextSequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(nextSequenceNumber), "The next sequence number must be at least 1.");
        }

        activeSessionId = sessionId;
        this.nextSequenceNumber = nextSequenceNumber;
    }

    /// <inheritdoc />
    public async Task<AssessmentSessionId> StartSessionAsync(CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("sessions", new StartSessionRequest(), cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<StartSessionResponse>(cancellationToken);
        if (body is null || string.IsNullOrWhiteSpace(body.SessionId))
        {
            throw new InvalidOperationException("The verifier returned an invalid session response.");
        }

        activeSessionId = new AssessmentSessionId(body.SessionId);
        nextSequenceNumber = 1;
        return activeSessionId.Value;
    }

    /// <inheritdoc />
    public async Task<CheckpointReceipt> SendCheckpointAsync(CancellationToken cancellationToken)
    {
        if (activeSessionId is null)
        {
            throw new InvalidOperationException("No active assessment session. Start a session first.");
        }

        var checkpoint = checkpointFactory(activeSessionId.Value, nextSequenceNumber);
        var request = new CheckpointRequest
        {
            SessionId = checkpoint.SessionId.Value,
            SequenceNumber = checkpoint.SequenceNumber,
            TimelineHeadHash = checkpoint.TimelineHeadHash
        };

        var response = await httpClient.PostAsJsonAsync($"sessions/{activeSessionId.Value}/checkpoints", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var receipt = await response.Content.ReadFromJsonAsync<CheckpointReceipt>(cancellationToken)
            ?? throw new InvalidOperationException("The verifier returned an invalid checkpoint receipt.");
        nextSequenceNumber = receipt.SequenceNumber + 1;
        return receipt;
    }

    /// <inheritdoc />
    public async Task<ReceiptChain> GetReceiptsAsync(CancellationToken cancellationToken)
    {
        if (activeSessionId is null)
        {
            throw new InvalidOperationException("No active assessment session. Start a session first.");
        }

        var response = await httpClient.GetAsync($"sessions/{activeSessionId.Value}/receipts", cancellationToken);
        response.EnsureSuccessStatusCode();

        var receiptChain = await response.Content.ReadFromJsonAsync<ReceiptChain>(cancellationToken)
            ?? throw new InvalidOperationException("The verifier returned an invalid receipt chain.");
        nextSequenceNumber = receiptChain.Receipts.Count + 1;
        return receiptChain;
    }
}
