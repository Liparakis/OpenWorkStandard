using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Ows.Core.Events;

namespace Ows.Core.Agent;

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

        if (options is null)
        {
            throw new InvalidOperationException("Tracking agent must be prepared before start.");
        }

        var timelinePath = Path.Combine(options.ProjectRootPath, OwsConstants.LocalFolderName, OwsConstants.TimelineFileName);
        var trackedFiles = Directory
            .EnumerateFiles(options.ProjectRootPath, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}{OwsConstants.LocalFolderName}{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        var previousEventHash = ReadLastEventHash(timelinePath);

        foreach (var path in trackedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(options.ProjectRootPath, path);
            var owsEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
            {
                EventType = OwsEventType.FileCreated,
                ProjectId = Path.GetFileName(options.ProjectRootPath),
                RelativePath = relativePath,
                ToolName = "ows watch",
                BytesChanged = new FileInfo(path).Length
            }, previousEventHash);

            File.AppendAllText(timelinePath, $"{JsonSerializer.Serialize(owsEvent)}{Environment.NewLine}");
            previousEventHash = owsEvent.EventHash;
        }

        Status = TrackingAgentStatus.Ready;
        logger.LogInformation("Tracked {FileCount} existing files for {ProjectRootPath}.", trackedFiles.Count(), options.ProjectRootPath);

        return Task.FromResult(new TrackingAgentOperationResult
        {
            Succeeded = true,
            Status = Status,
            Message = "OWS watch completed one scan."
        });
    }

    /// <summary>
    /// Reads the hash of the last timeline event, or the genesis value when the timeline is empty.
    /// </summary>
    /// <param name="timelinePath">The path to the local timeline file.</param>
    /// <returns>The previous event hash to use for the next appended event.</returns>
    private static string ReadLastEventHash(string timelinePath)
    {
        string? lastNonEmptyLine = null;
        foreach (var line in File.ReadLines(timelinePath))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lastNonEmptyLine = line;
            }
        }

        if (string.IsNullOrWhiteSpace(lastNonEmptyLine))
        {
            return OwsEventChain.GenesisPreviousEventHash;
        }

        var lastEvent = JsonSerializer.Deserialize<OwsEvent>(lastNonEmptyLine)
            ?? throw new JsonException("Timeline event deserialized to null.");

        return lastEvent.EventHash;
    }
}
