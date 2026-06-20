using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Ows.Core.Events;

namespace Ows.Core.Agent;

/// <summary>
/// Provides the local tracking agent that performs an initial project scan and then
/// watches for file-system changes, appending chained provenance events to the timeline.
/// </summary>
public sealed class LocalTrackingAgent(ILogger<LocalTrackingAgent> logger) : ITrackingAgent
{
    private TrackingAgentOptions? _options;

    // Serializes concurrent timeline writes so events are never interleaved.
    private readonly SemaphoreSlim _timelineLock = new(1, 1);

    /// <inheritdoc />
    public TrackingAgentStatus Status { get; private set; } = TrackingAgentStatus.Idle;

    /// <inheritdoc />
    public Task<TrackingAgentOperationResult> PrepareAsync(TrackingAgentOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        _options = options;
        _ = new SqliteConnectionStringBuilder { DataSource = options.DatabasePath }.ToString();
        Status = TrackingAgentStatus.Ready;

        logger.LogInformation("Prepared tracking agent for {ProjectRootPath}.", options.ProjectRootPath);

        return Task.FromResult(new TrackingAgentOperationResult
        {
            Succeeded = true,
            Status = Status
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// This method runs in two sequential phases:
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       <b>Initial scan</b> — all existing files in the project are appended to the timeline
    ///       as <see cref="OwsEventType.FileCreated"/> events, matching the previous one-shot behaviour.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>Continuous watch</b> — <see cref="OwsFileWatcher"/> yields debounced
    ///       <see cref="FileWatchEvent"/> records until <paramref name="cancellationToken"/> is
    ///       cancelled. Each event is translated into a chained <see cref="OwsEvent"/> and appended
    ///       to the timeline. The method blocks until the token is cancelled.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    public async Task<TrackingAgentOperationResult> StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_options is null)
        {
            throw new InvalidOperationException("Tracking agent must be prepared before start.");
        }

        var timelinePath = Path.Combine(_options.ProjectRootPath, OwsConstants.LocalFolderName,
            OwsConstants.TimelineFileName);
        var projectId = Path.GetFileName(_options.ProjectRootPath);

        var scanned = await PerformInitialScanAsync(timelinePath, projectId, cancellationToken);
        logger.LogInformation("Initial scan complete — {FileCount} file(s) recorded.", scanned);
        Status = TrackingAgentStatus.Watching;

        var watcher = new OwsFileWatcher(
            _options.ProjectRootPath,
            ShouldExclude,
            _options.WatcherOptions);

        await using (watcher)
        {
            await foreach (var watchEvent in watcher.WatchAsync(cancellationToken))
            {
                await AppendWatchEventAsync(timelinePath, projectId, watchEvent, cancellationToken);
            }
        }

        Status = TrackingAgentStatus.Stopped;

        return new TrackingAgentOperationResult
        {
            Succeeded = true,
            Status = Status
        };
    }

    /// <summary>
    /// Performs a one-shot scan of the project tree, appending <see cref="OwsEventType.FileCreated"/>
    /// events for every file that is not already excluded.
    /// </summary>
    private async Task<int> PerformInitialScanAsync(string timelinePath, string projectId,
        CancellationToken cancellationToken)
    {
        var trackedFiles = Directory
            .EnumerateFiles(_options!.ProjectRootPath, "*", SearchOption.AllDirectories)
            .Where(path => !ShouldExclude(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _timelineLock.WaitAsync(cancellationToken);
        try
        {
            var previousEventHash = OwsEventChain.ReadLastEventHash(timelinePath);
            foreach (var path in trackedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var owsEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
                {
                    EventType = OwsEventType.FileCreated,
                    ProjectId = projectId,
                    RelativePath = Path.GetRelativePath(_options.ProjectRootPath, path),
                    ToolName = "ows watch",
                    BytesChanged = new FileInfo(path).Length
                }, previousEventHash);

                await File.AppendAllTextAsync(timelinePath,
                    $"{JsonSerializer.Serialize(owsEvent)}{Environment.NewLine}", cancellationToken);
                previousEventHash = owsEvent.EventHash;
            }
        }
        finally
        {
            _timelineLock.Release();
        }

        return trackedFiles.Count;
    }

    /// <summary>
    /// Translates a debounced <see cref="FileWatchEvent"/> into a chained <see cref="OwsEvent"/>
    /// and appends it to the timeline under the write lock.
    /// </summary>
    private async Task AppendWatchEventAsync(string timelinePath, string projectId,
        FileWatchEvent watchEvent, CancellationToken cancellationToken)
    {
        var eventType = watchEvent.ChangeKind switch
        {
            FileChangeKind.Created => OwsEventType.FileCreated,
            FileChangeKind.Modified => OwsEventType.FileModified,
            FileChangeKind.Deleted => OwsEventType.FileDeleted,
            FileChangeKind.Renamed => OwsEventType.FileCreated,
            _ => OwsEventType.FileModified
        };

        long? bytesChanged = null;
        if (watchEvent.ChangeKind != FileChangeKind.Deleted)
        {
            var absolutePath = Path.Combine(_options!.ProjectRootPath, watchEvent.RelativePath);
            try
            {
                bytesChanged = new FileInfo(absolutePath).Length;
            }
            catch (IOException)
            {
                // File may have disappeared between detection and stat — leave null.
            }
        }

        await _timelineLock.WaitAsync(cancellationToken);
        try
        {
            var previousEventHash = OwsEventChain.ReadLastEventHash(timelinePath);
            var owsEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
            {
                EventType = eventType,
                ProjectId = projectId,
                RelativePath = watchEvent.RelativePath,
                ToolName = "ows watch",
                BytesChanged = bytesChanged
            }, previousEventHash);

            await File.AppendAllTextAsync(timelinePath,
                $"{JsonSerializer.Serialize(owsEvent)}{Environment.NewLine}", cancellationToken);

            logger.LogDebug("Appended {EventType} for {RelativePath}.", eventType, watchEvent.RelativePath);
        }
        finally
        {
            _timelineLock.Release();
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the absolute path falls inside the <c>.ows</c>
    /// local metadata folder and should not be tracked.
    /// </summary>
    private bool ShouldExclude(string absolutePath) =>
        absolutePath.Contains(
            $"{Path.DirectorySeparatorChar}{OwsConstants.LocalFolderName}{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal) ||
        absolutePath.Contains(
            $"/{OwsConstants.LocalFolderName}/",
            StringComparison.Ordinal);
}