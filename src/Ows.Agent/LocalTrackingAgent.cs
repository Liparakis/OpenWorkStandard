using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Ows.Agent;

/// <summary>
/// Provides the initial local tracking agent skeleton for future file watching work.
/// </summary>
public sealed class LocalTrackingAgent(ILogger<LocalTrackingAgent> logger) : ITrackingAgent
{
    private TrackingAgentOptions? options;

    /// <inheritdoc />
    public TrackingAgentStatus Status { get; private set; } = TrackingAgentStatus.Idle;

    /// <inheritdoc />
    public Task<TrackingAgentOperationResult> PrepareAsync(TrackingAgentOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        this.options = options;
        _ = new SqliteConnectionStringBuilder { DataSource = options.DatabasePath }.ToString();
        Status = TrackingAgentStatus.Ready;

        logger.LogInformation("Prepared tracking agent skeleton for {ProjectRootPath}.", options.ProjectRootPath);

        return Task.FromResult(new TrackingAgentOperationResult
        {
            Succeeded = true,
            Status = Status,
            Message = "OWS watch agent skeleton prepared. Active tracking is not implemented yet."
        });
    }

    /// <inheritdoc />
    public Task<TrackingAgentOperationResult> StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Status = TrackingAgentStatus.NotImplemented;
        logger.LogInformation("Tracking start requested for {ProjectRootPath}.", options?.ProjectRootPath ?? "<unconfigured>");

        return Task.FromResult(new TrackingAgentOperationResult
        {
            Succeeded = false,
            Status = Status,
            Message = "OWS watch: not implemented yet"
        });
    }
}
