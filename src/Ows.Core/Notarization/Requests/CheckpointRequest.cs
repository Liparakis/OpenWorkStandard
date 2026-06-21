namespace Ows.Core.Notarization;

/// <summary>
/// Represents the minimal request body for submitting a checkpoint to the verifier.
/// </summary>
public sealed record CheckpointRequest {
    /// <summary>
    /// Gets the session identifier associated with the checkpoint.
    /// </summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the checkpoint sequence number within the session.
    /// </summary>
    public int SequenceNumber { get; init; }

    /// <summary>
    /// Gets the timeline head hash being notarized.
    /// </summary>
    public string TimelineHeadHash { get; init; } = string.Empty;

    /// <summary>
    /// Validates the request fields needed by the verifier checkpoint endpoint.
    /// </summary>
    /// <param name="routeSessionId">The session identifier from the route.</param>
    /// <param name="idempotencyKey">The optional idempotency key header.</param>
    /// <returns>The validation error message when invalid; otherwise <see langword="null"/>.</returns>
    public string? GetValidationError(string routeSessionId, string? idempotencyKey) {
        if (string.IsNullOrWhiteSpace(routeSessionId)) {
            return "Route session id is required.";
        }

        if (string.IsNullOrWhiteSpace(SessionId)) {
            return "Payload session id is required.";
        }

        if (!string.Equals(routeSessionId, SessionId, StringComparison.Ordinal)) {
            return "Route session id does not match payload session id.";
        }

        if (SequenceNumber < 1) {
            return "Checkpoint sequence number must be at least 1.";
        }

        if (string.IsNullOrWhiteSpace(TimelineHeadHash)) {
            return "Timeline head hash is required.";
        }

        if (idempotencyKey is not null && string.IsNullOrWhiteSpace(idempotencyKey)) {
            return "Idempotency-Key header must not be empty when provided.";
        }

        return null;
    }
}
