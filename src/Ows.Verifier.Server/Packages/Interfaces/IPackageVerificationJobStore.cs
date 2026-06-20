namespace Ows.Verifier.Server;

/// <summary>
/// Persists package verification jobs so uploads survive server restarts.
/// </summary>
internal interface IPackageVerificationJobStore
{
    /// <summary>
    /// Initializes the backing store.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Queues a verification job unless one is already pending or running.
    /// </summary>
    Task<PackageVerificationJobRecord> QueueAsync(
        string packageId,
        string? requestedByApiKeyId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Starts the next runnable job and returns it, or null if none is ready.
    /// </summary>
    Task<PackageVerificationJobRecord?> TryStartNextAsync(
        TimeSpan staleRunningThreshold,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the latest job for one package.
    /// </summary>
    Task<PackageVerificationJobRecord?> GetLatestForPackageAsync(string packageId, CancellationToken cancellationToken);

    /// <summary>
    /// Completes a running job.
    /// </summary>
    Task CompleteAsync(
        string jobId,
        string status,
        string? resultJson,
        string? lastError,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns aggregate job counts for diagnostics.
    /// </summary>
    Task<PackageVerificationJobSummary> GetSummaryAsync(CancellationToken cancellationToken);
}
