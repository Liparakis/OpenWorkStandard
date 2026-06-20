namespace Ows.Verifier.Server;

/// <summary>
/// Persists safe verifier audit events for operator diagnostics.
/// </summary>
internal interface IVerifierAuditStore
{
    /// <summary>
    /// Initializes the backing store.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Appends one audit event.
    /// </summary>
    Task AppendAsync(VerifierAuditEvent auditEvent, CancellationToken cancellationToken);

    /// <summary>
    /// Queries audit events using the supplied filters.
    /// </summary>
    Task<IReadOnlyList<VerifierAuditEvent>> QueryAsync(VerifierAuditQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Returns safe aggregate counts for lightweight diagnostics.
    /// </summary>
    Task<VerifierAuditSummary> GetSummaryAsync(CancellationToken cancellationToken);
}
