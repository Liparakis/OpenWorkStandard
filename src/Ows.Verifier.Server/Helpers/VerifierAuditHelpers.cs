namespace Ows.Verifier.Server;

/// <summary>
/// Provides utility helpers for writing verifier audit events and building metadata dictionaries.
/// </summary>
internal static class VerifierAuditHelpers
{
    /// <summary>
    /// Builds a compact metadata dictionary, ignoring null or whitespace keys and values.
    /// </summary>
    /// <param name="pairs">An array of key-value tuples.</param>
    /// <returns>A read-only dictionary containing the non-blank metadata key-value pairs.</returns>
    public static IReadOnlyDictionary<string, string?> CreateMetadata(params (string Key, string? Value)[] pairs)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                metadata[key] = value;
            }
        }

        return metadata;
    }

    /// <summary>
    /// Appends an audit event to the audit store and outputs a structured log line without sensitive data.
    /// </summary>
    /// <param name="auditStore">The audit store to persist the event to.</param>
    /// <param name="logger">The logger instance to write structured logs to.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="eventType">The type of the audit event (e.g. auth.failed, session.created).</param>
    /// <param name="result">The outcome outcome string (e.g. Accepted, Rejected, Created).</param>
    /// <param name="access">The optional caller access context.</param>
    /// <param name="institutionId">The optional institution scope identifier.</param>
    /// <param name="sessionId">The optional session identifier.</param>
    /// <param name="packageId">The optional package submission identifier.</param>
    /// <param name="assessmentId">The optional assessment identifier.</param>
    /// <param name="metadata">Additional custom metadata associated with the event.</param>
    /// <param name="actorKeyPrefix">The non-sensitive display prefix of the API key used by the caller.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task WriteAuditEventAsync(
        IVerifierAuditStore auditStore,
        ILogger logger,
        HttpContext context,
        string eventType,
        string result,
        VerifierAccessContext? access = null,
        string? institutionId = null,
        string? sessionId = null,
        string? packageId = null,
        string? assessmentId = null,
        IReadOnlyDictionary<string, string?>? metadata = null,
        string? actorKeyPrefix = null,
        CancellationToken cancellationToken = default)
    {
        var auditEvent = new VerifierAuditEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            EventType = eventType,
            ActorKeyId = access?.KeyId,
            ActorKeyPrefix = actorKeyPrefix ?? access?.KeyPrefix,
            ActorRole = access?.Role,
            ActorUserId = access?.ActorUserId,
            ActorEmail = access?.ActorEmail,
            ActorDisplayName = access?.ActorDisplayName,
            InstitutionId = institutionId ?? access?.InstitutionId,
            SessionId = sessionId,
            PackageId = packageId,
            AssessmentId = assessmentId,
            Result = result,
            Metadata = metadata ?? CreateMetadata()
        };

        try
        {
            await auditStore.AppendAsync(auditEvent, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Verifier audit persistence failed for eventType={EventType} requestId={RequestId}.",
                auditEvent.EventType,
                VerifierServerHelpers.GetRequestId(context));
        }

        logger.LogInformation(
            "Verifier audit {EventType} result={Result} requestId={RequestId} authType={AuthType} role={Role} institutionId={InstitutionId} sessionId={SessionId} packageId={PackageId} assessmentId={AssessmentId} keyPrefix={KeyPrefix} actorUserId={ActorUserId} actorEmail={ActorEmail} actorDisplayName={ActorDisplayName}",
            auditEvent.EventType,
            auditEvent.Result,
            VerifierServerHelpers.GetRequestId(context),
            access?.AuthenticationType ?? "Anonymous",
            auditEvent.ActorRole ?? "Anonymous",
            auditEvent.InstitutionId,
            auditEvent.SessionId,
            auditEvent.PackageId,
            auditEvent.AssessmentId,
            auditEvent.ActorKeyPrefix,
            auditEvent.ActorUserId,
            auditEvent.ActorEmail,
            auditEvent.ActorDisplayName);
    }
}