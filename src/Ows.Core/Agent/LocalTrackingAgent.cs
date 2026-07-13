using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Ows.Core.Agent.Scanning;
using Ows.Core.Agent.Snapshot;
using Ows.Core.Agent.Timeline;
using Ows.Core.Agent.Watcher;
using Ows.Core.Events;
using Ows.Core.Hashing;
using Ows.Core.Ignore;

namespace Ows.Core.Agent;

/// <summary>
/// Provides the local tracking agent that performs a project scan (baseline or recovery) and then
/// watches for file-system changes, appending chained provenance events to the timeline.
/// </summary>
public sealed class LocalTrackingAgent(ILogger<LocalTrackingAgent> logger) {
    /// <summary>
    /// The runtime options prepared for the tracking agent containing configuration settings such as paths, directories to exclude, and polling requirements.
    /// </summary>
    private TrackingAgentOptions? _options;

    /// <summary>
    /// The semaphore lock used to synchronize concurrent timeline file append operations, preventing overlap or corruption of the event stream.
    /// </summary>
    private readonly SemaphoreSlim _timelineLock = new(1, 1);

    /// <summary>
    /// The ignore rules shared by recovery scans and the active watcher.
    /// </summary>
    private OwsIgnoreEngine _ignoreEngine = new();

    /// <summary>
    /// The currently tracked observed snapshot state containing the project files, hashes, sizes, and timestamps.
    /// </summary>
    private ObservedSnapshot _currentSnapshot = new();

    /// <summary>
    /// Gets or sets the current status of the tracking agent.
    /// </summary>
    private TrackingAgentStatus Status { get; set; } = TrackingAgentStatus.Idle;

    /// <summary>
    /// Prepares the tracking agent for a project by loading ignore rules and setting its ready state.
    /// </summary>
    /// <param name="options">The tracking and watcher options for the project.</param>
    /// <param name="cancellationToken">The token used to cancel preparation.</param>
    /// <returns>The result of preparing the tracking agent.</returns>
    public Task<TrackingAgentOperationResult> PrepareAsync(
        TrackingAgentOptions options,
        CancellationToken cancellationToken
    ) {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        _options = options;
        _ignoreEngine = OwsIgnoreEngine.Load(
            options.ProjectRootPath,
            options.WatcherOptions.ExcludeDirectories
        );
        _ = new SqliteConnectionStringBuilder { DataSource = options.DatabasePath }.ToString();
        Status = TrackingAgentStatus.Ready;

        logger.LogInformation("Prepared tracking agent for {ProjectRootPath}.", options.ProjectRootPath);

        return Task.FromResult(
            new TrackingAgentOperationResult {
                Succeeded = true,
                Status = Status
            }
        );
    }

    /// <summary>
    /// Runs the recovery scan and watches the initialized project until cancellation.
    /// </summary>
    /// <param name="cancellationToken">The token used to stop the tracking agent.</param>
    /// <returns>The result produced when the tracking agent stops.</returns>
    public async Task<TrackingAgentOperationResult> StartAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (_options is null) {
            throw new InvalidOperationException("Tracking agent must be prepared before start.");
        }

        var timelinePath = Path.Combine(
            _options.ProjectRootPath, OwsConstants.LocalFolderName,
            OwsConstants.TimelineFileName
        );
        var projectId = Path.GetFileName(_options.ProjectRootPath);

        // Run recovery scan
        await PerformRecoveryScanAsync(timelinePath, projectId, cancellationToken);
        Status = TrackingAgentStatus.Watching;

        var watcher = new OwsFileWatcher(
            _options.ProjectRootPath,
            ShouldExclude,
            _options.WatcherOptions
        );

        await using (watcher) {
            await foreach (var watchEvent in watcher.WatchAsync(cancellationToken)) {
                await AppendWatchEventAsync(timelinePath, projectId, watchEvent, cancellationToken);
            }
        }

        Status = TrackingAgentStatus.Stopped;

        return new TrackingAgentOperationResult {
            Succeeded = true,
            Status = Status
        };
    }

    /// <summary>
    /// Performs a recovery scan of the project files to detect and record changes that occurred since the last recorded snapshot.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous recovery scan operation.</returns>
    /// <param name="projectId">The identifier of the project.</param>
    /// <param name="timelinePath">The file path to the project's timeline log.</param>
    /// <param name="cancellationToken">The token used to cancel the recovery scan.</param>
    private async Task PerformRecoveryScanAsync(
        string timelinePath,
        string projectId,
        CancellationToken cancellationToken
    ) {
        var localFolder = Path.Combine(_options!.ProjectRootPath, OwsConstants.LocalFolderName);
        var snapshotPath = Path.Combine(localFolder, OwsConstants.ObservedSnapshotFileName);
        var hashService = new Sha256HashService();

        var currentFiles = ProjectFileScanner.ScanCurrentFiles(
            _options.ProjectRootPath,
            _ignoreEngine, hashService
        );

        var loadResult = await ObservedSnapshotStore.LoadSnapshotAsync(snapshotPath, logger, cancellationToken);
        var previousSnapshot = loadResult.Snapshot;
        var computedSnapshotHash = loadResult.ComputedSnapshotHash;
        var snapshotUnreadable = loadResult.SnapshotUnreadable;

        await _timelineLock.WaitAsync(cancellationToken);
        try {
            var previousEventHash = TimelineEventAppender.ReadLastEventHash(timelinePath);
            var hadPriorTimeline = File.Exists(timelinePath) &&
                                   File.ReadLines(timelinePath).Any(line => !string.IsNullOrWhiteSpace(line));
            var committedSnapshotHash = hadPriorTimeline ? FindLatestCommittedSnapshotHash(timelinePath) : null;
            var snapshotHashMismatch = previousSnapshot != null &&
                                       !string.IsNullOrWhiteSpace(committedSnapshotHash) &&
                                       !string.Equals(
                                           computedSnapshotHash, committedSnapshotHash,
                                           StringComparison.OrdinalIgnoreCase
                                       );
            var snapshotUnbound = previousSnapshot != null &&
                                  hadPriorTimeline &&
                                  string.IsNullOrWhiteSpace(committedSnapshotHash);

            if (previousSnapshot == null && hadPriorTimeline) {
                var now = DateTimeOffset.UtcNow;
                var gapStartedAt = now;
                var gapDurationMsVal = (long) (now - gapStartedAt).TotalMilliseconds;
                var previousStateVal = DeterminePreviousWatcherState(timelinePath, _options.WasInterrupted);

                if (_options.WasInterrupted) {
                    var interruptedEvent = WatcherLifecycleEventBuilder.BuildWatcherInterrupted(
                        projectId,
                        _options.InterruptedState?.Pid.ToString(), "stale_pid"
                    );
                    previousEventHash = await TimelineEventAppender.AppendEventAsync(
                        timelinePath, interruptedEvent,
                        previousEventHash, cancellationToken
                    );

                    var recoveredEvent =
                        WatcherLifecycleEventBuilder.BuildWatcherRecovered(
                            projectId, "crash_recovery",
                            gapDurationMsVal
                        );
                    previousEventHash = await TimelineEventAppender.AppendEventAsync(
                        timelinePath, recoveredEvent,
                        previousEventHash, cancellationToken
                    );
                } else {
                    var startedEvent = WatcherLifecycleEventBuilder.BuildWatcherStarted(projectId, "user_restart");
                    previousEventHash = await TimelineEventAppender.AppendEventAsync(
                        timelinePath, startedEvent,
                        previousEventHash, cancellationToken
                    );
                }

                var baselineStateStr = snapshotUnreadable ? "corrupt_snapshot" : "missing_snapshot";
                var gapEvent = WatcherLifecycleEventBuilder.BuildObservationGapDetected(
                    projectId, gapStartedAt, now, gapDurationMsVal, previousStateVal,
                    _options.WasInterrupted ? "crash_recovery" : "user_start", baselineStateStr
                );

                previousEventHash =
                    await TimelineEventAppender.AppendEventAsync(
                        timelinePath, gapEvent, previousEventHash,
                        cancellationToken
                    );
            } else if (snapshotHashMismatch || snapshotUnbound) {
                var now = DateTimeOffset.UtcNow;
                var gapStartedAt = now;
                var gapDurationMsVal = (long) (now - gapStartedAt).TotalMilliseconds;
                var previousStateVal = DeterminePreviousWatcherState(timelinePath, _options.WasInterrupted);

                previousEventHash = await AppendRecoveryLifecycleEventsAsync(
                    timelinePath,
                    projectId,
                    previousEventHash,
                    gapDurationMsVal,
                    cancellationToken
                );

                var baselineState = snapshotHashMismatch ? "snapshot_hash_mismatch" : "unbound_snapshot";
                var gapEvent = WatcherLifecycleEventBuilder.BuildObservationGapDetected(
                    projectId, gapStartedAt, now, gapDurationMsVal, previousStateVal,
                    _options.WasInterrupted ? "crash_recovery" : "user_start", baselineState,
                    committedSnapshotHash, computedSnapshotHash
                );

                previousEventHash =
                    await TimelineEventAppender.AppendEventAsync(
                        timelinePath, gapEvent, previousEventHash,
                        cancellationToken
                    );
            } else if (previousSnapshot == null) {
                // 1. Initial/first run: Emit file creation baseline events and WatcherStarted
                foreach (var fileState in currentFiles.Values) {
                    var owsEvent = new OwsEvent {
                        EventType = OwsEventType.FileCreated,
                        ProjectId = projectId,
                        RelativePath = fileState.RelativePath,
                        ToolName = "OWS Agent",
                        BytesChanged = fileState.Size,
                        Metadata = new Dictionary<string, string> {
                            { "source", "initial_baseline" },
                            { "usedForTrust", "false" }
                        }
                    };

                    previousEventHash = await TimelineEventAppender.AppendEventAsync(
                        timelinePath, owsEvent,
                        previousEventHash, cancellationToken
                    );
                }

                var startedEvent = WatcherLifecycleEventBuilder.BuildWatcherStarted(projectId, "user_start");
                previousEventHash = await TimelineEventAppender.AppendEventAsync(
                    timelinePath, startedEvent,
                    previousEventHash, cancellationToken
                );
            } else {
                // 2. Resume / recovery scan
                var now = DateTimeOffset.UtcNow;

                // Gap computation parameters
                var gapStartedAt = previousSnapshot.ObservedAt;

                var gapEndedAt = now;
                var gapDurationMsVal = (long) (gapEndedAt - gapStartedAt).TotalMilliseconds;

                var previousStateVal = DeterminePreviousWatcherState(timelinePath, _options.WasInterrupted);

                previousEventHash = await AppendRecoveryLifecycleEventsAsync(
                    timelinePath,
                    projectId,
                    previousEventHash,
                    gapDurationMsVal,
                    cancellationToken
                );

                // Compare snapshot to current state
                var scanResult = RecoveryScanService.CompareSnapshots(previousSnapshot, currentFiles);
                if (scanResult.HasDifferences) {
                    // Emit ObservationGapDetected
                    var gapEvent = WatcherLifecycleEventBuilder.BuildObservationGapDetected(
                        projectId, gapStartedAt, gapEndedAt, gapDurationMsVal, previousStateVal,
                        _options.WasInterrupted ? "crash_recovery" : "user_start", "active_snapshot"
                    );

                    previousEventHash = await TimelineEventAppender.AppendEventAsync(
                        timelinePath, gapEvent,
                        previousEventHash, cancellationToken
                    );

                    // Created files
                    foreach (var file in scanResult.CreatedFiles) {
                        var isLarge = RecoveryScanService.IsLargeChange(file.Size, file.LineCount);
                        var changeEvent = new OwsEvent {
                            EventType = isLarge
                                ? OwsEventType.LargeUnobservedChangeDetected
                                : OwsEventType.UnobservedChangeDetected,
                            ProjectId = projectId,
                            RelativePath = file.RelativePath,
                            ToolName = "OWS Agent",
                            BytesChanged = file.Size,
                            Metadata = new Dictionary<string, string> {
                                { "changeKind", "Created" },
                                { "relativePath", file.RelativePath },
                                { "currentHash", file.FileHash },
                                { "currentSize", file.Size.ToString() },
                                { "bytesDelta", file.Size.ToString() },
                                { "lineDeltaEstimate", file.LineCount.ToString() },
                                { "gapDurationMs", gapDurationMsVal.ToString() }
                            }
                        };

                        previousEventHash = await TimelineEventAppender.AppendEventAsync(
                            timelinePath, changeEvent,
                            previousEventHash, cancellationToken
                        );
                    }

                    // Modified files
                    foreach (var (prev, curr) in scanResult.ModifiedFiles) {
                        var bytesDelta = curr.Size - prev.Size;
                        var lineDelta = curr.LineCount - prev.LineCount;
                        var isLarge = RecoveryScanService.IsLargeChange(bytesDelta, lineDelta);

                        var changeEvent = new OwsEvent {
                            EventType = isLarge
                                ? OwsEventType.LargeUnobservedChangeDetected
                                : OwsEventType.UnobservedChangeDetected,
                            ProjectId = projectId,
                            RelativePath = curr.RelativePath,
                            ToolName = "OWS Agent",
                            BytesChanged = bytesDelta,
                            Metadata = new Dictionary<string, string> {
                                { "changeKind", "Modified" },
                                { "relativePath", curr.RelativePath },
                                { "previousHash", prev.FileHash },
                                { "currentHash", curr.FileHash },
                                { "previousSize", prev.Size.ToString() },
                                { "currentSize", curr.Size.ToString() },
                                { "bytesDelta", bytesDelta.ToString() },
                                { "lineDeltaEstimate", lineDelta.ToString() },
                                { "gapDurationMs", gapDurationMsVal.ToString() }
                            }
                        };

                        previousEventHash = await TimelineEventAppender.AppendEventAsync(
                            timelinePath, changeEvent,
                            previousEventHash, cancellationToken
                        );
                    }

                    // Deleted files
                    foreach (var file in scanResult.DeletedFiles) {
                        var bytesDelta = -file.Size;
                        var lineDelta = -file.LineCount;
                        var isLarge = RecoveryScanService.IsLargeChange(bytesDelta, lineDelta);

                        var changeEvent = new OwsEvent {
                            EventType = isLarge
                                ? OwsEventType.LargeUnobservedChangeDetected
                                : OwsEventType.UnobservedChangeDetected,
                            ProjectId = projectId,
                            RelativePath = file.RelativePath,
                            ToolName = "OWS Agent",
                            BytesChanged = bytesDelta,
                            Metadata = new Dictionary<string, string> {
                                { "changeKind", "Deleted" },
                                { "relativePath", file.RelativePath },
                                { "previousHash", file.FileHash },
                                { "previousSize", file.Size.ToString() },
                                { "currentSize", "0" },
                                { "bytesDelta", bytesDelta.ToString() },
                                { "lineDeltaEstimate", lineDelta.ToString() },
                                { "gapDurationMs", gapDurationMsVal.ToString() }
                            }
                        };

                        previousEventHash = await TimelineEventAppender.AppendEventAsync(
                            timelinePath, changeEvent,
                            previousEventHash, cancellationToken
                        );
                    }
                }
            }

            // Save the updated snapshot
            _currentSnapshot = new ObservedSnapshot {
                ObservedAt = DateTimeOffset.UtcNow,
                Files = currentFiles
            };
            await ObservedSnapshotStore.SaveSnapshotAtomicallyAsync(snapshotPath, _currentSnapshot, cancellationToken);

            var snapshotUpdatedEvent = WatcherLifecycleEventBuilder.BuildSnapshotUpdated(
                projectId,
                SnapshotHashCalculator.ComputeHash(_currentSnapshot),
                _currentSnapshot.Files.Count,
                _currentSnapshot.ObservedAt,
                previousSnapshot == null ? "initial_baseline" : "recovery_scan",
                computedSnapshotHash
            );
            await TimelineEventAppender.AppendEventAsync(
                timelinePath, snapshotUpdatedEvent, previousEventHash,
                CancellationToken.None
            );
        } finally {
            _timelineLock.Release();
        }
    }

    /// <summary>
    /// Appends watcher lifecycle events (such as interrupted, recovered, or started) to the timeline log during recovery.
    /// </summary>
    /// <returns>A <see cref="Task{TResult}"/> returning the hash of the last appended event.</returns>
    /// <param name="gapDurationMsVal">The calculated inactivity gap duration in milliseconds.</param>
    /// <param name="previousEventHash">The hash of the previous event in the timeline log.</param>
    /// <param name="timelinePath">The file path to the timeline log.</param>
    /// <param name="projectId">The identifier of the project.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    private async Task<string> AppendRecoveryLifecycleEventsAsync(
        string timelinePath,
        string projectId,
        string previousEventHash,
        long gapDurationMsVal,
        CancellationToken cancellationToken
    ) {
        if (_options!.WasInterrupted) {
            var interruptedEvent = WatcherLifecycleEventBuilder.BuildWatcherInterrupted(
                projectId, _options.InterruptedState?.Pid.ToString(), "stale_pid"
            );
            previousEventHash = await TimelineEventAppender.AppendEventAsync(
                timelinePath, interruptedEvent,
                previousEventHash, cancellationToken
            );

            var recoveredEvent = WatcherLifecycleEventBuilder.BuildWatcherRecovered(
                projectId, "crash_recovery", gapDurationMsVal
            );
            return await TimelineEventAppender.AppendEventAsync(
                timelinePath, recoveredEvent, previousEventHash,
                cancellationToken
            );
        }

        var startedEvent = WatcherLifecycleEventBuilder.BuildWatcherStarted(projectId, "user_restart");
        return await TimelineEventAppender.AppendEventAsync(
            timelinePath, startedEvent, previousEventHash,
            cancellationToken
        );
    }

    /// <summary>
    /// Reads the timeline log in reverse to find the hash of the latest successfully committed snapshot.
    /// </summary>
    /// <returns>The latest committed snapshot hash as a string, or <see langword="null"/> if not found or the timeline does not exist.</returns>
    /// <param name="timelinePath">The file path to the timeline log.</param>
    private static string? FindLatestCommittedSnapshotHash(string timelinePath) {
        if (!File.Exists(timelinePath)) {
            return null;
        }

        foreach (var line in File.ReadLines(timelinePath).Reverse()) {
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }

            try {
                var owsEvent = JsonSerializer.Deserialize<OwsEvent>(line);
                if (owsEvent?.EventType == OwsEventType.SnapshotUpdated &&
                    owsEvent.Metadata.TryGetValue("snapshotHash", out var snapshotHash) &&
                    !string.IsNullOrWhiteSpace(snapshotHash)) {
                    return snapshotHash;
                }
            } catch {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Maps a file watcher change event to an OWS timeline event, updates the snapshot, and appends the events to the timeline log.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// <param name="projectId">The identifier of the project.</param>
    /// <param name="timelinePath">The file path to the timeline log.</param>
    /// <param name="watchEvent">The file change event detected by the file watcher.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    private async Task AppendWatchEventAsync(
        string timelinePath,
        string projectId,
        FileWatchEvent watchEvent,
        CancellationToken cancellationToken
    ) {
        var eventType = watchEvent.ChangeKind switch {
            FileChangeKind.Created => OwsEventType.FileCreated,
            FileChangeKind.Modified => OwsEventType.FileModified,
            FileChangeKind.Deleted => OwsEventType.FileDeleted,
            FileChangeKind.Renamed => OwsEventType.FileCreated,
            _ => OwsEventType.FileModified
        };

        long? bytesChanged = null;
        var absolutePath = Path.Combine(_options!.ProjectRootPath, watchEvent.RelativePath);
        if (watchEvent.ChangeKind != FileChangeKind.Deleted) {
            try {
                bytesChanged = new FileInfo(absolutePath).Length;
            } catch (IOException) {
            }
        }

        await _timelineLock.WaitAsync(cancellationToken);
        try {
            var previousEventHash = TimelineEventAppender.ReadLastEventHash(timelinePath);
            var previousSnapshotHash = SnapshotHashCalculator.ComputeHash(_currentSnapshot);
            var owsEvent = new OwsEvent {
                EventType = eventType,
                ProjectId = projectId,
                RelativePath = watchEvent.RelativePath,
                ToolName = "OWS Agent",
                BytesChanged = bytesChanged
            };

            var newEventHash =
                await TimelineEventAppender.AppendEventAsync(
                    timelinePath, owsEvent, previousEventHash,
                    cancellationToken
                );

            logger.LogDebug("Appended {EventType} for {RelativePath}.", eventType, watchEvent.RelativePath);

            // Update snapshot
            var hashService = new Sha256HashService();
            if (watchEvent.ChangeKind == FileChangeKind.Deleted) {
                _currentSnapshot.Files.Remove(watchEvent.RelativePath);
            } else {
                try {
                    if (File.Exists(absolutePath)) {
                        var info = new FileInfo(absolutePath);
                        _currentSnapshot.Files[watchEvent.RelativePath] = new ObservedFileState {
                            RelativePath = watchEvent.RelativePath,
                            FileHash = ProjectFileScanner.GetFileHash(absolutePath, hashService),
                            Size = info.Length,
                            LineCount = ProjectFileScanner.GetLineCountEstimate(absolutePath),
                            LastWriteTime = info.LastWriteTimeUtc,
                            ObservedAt = DateTimeOffset.UtcNow
                        };
                    }
                } catch {
                    /*ignored*/
                }
            }

            _currentSnapshot.ObservedAt = DateTimeOffset.UtcNow;
            var localFolder = Path.Combine(_options!.ProjectRootPath, OwsConstants.LocalFolderName);
            var snapshotPath = Path.Combine(localFolder, OwsConstants.ObservedSnapshotFileName);
            await ObservedSnapshotStore.SaveSnapshotAtomicallyAsync(snapshotPath, _currentSnapshot, cancellationToken);

            var snapshotUpdatedEvent = WatcherLifecycleEventBuilder.BuildSnapshotUpdated(
                projectId,
                SnapshotHashCalculator.ComputeHash(_currentSnapshot),
                _currentSnapshot.Files.Count,
                _currentSnapshot.ObservedAt,
                "watcher_event",
                previousSnapshotHash
            );
            await TimelineEventAppender.AppendEventAsync(
                timelinePath, snapshotUpdatedEvent, newEventHash,
                CancellationToken.None
            );
        } finally {
            _timelineLock.Release();
        }
    }

    /// <summary>
    /// Determines whether a file or directory at the specified absolute path should be excluded based on ignore rules.
    /// </summary>
    /// <returns><see langword="true"/> if the path should be excluded; otherwise, <see langword="false"/>.</returns>
    /// <param name="absolutePath">The absolute path of the file or directory to check.</param>
    private bool ShouldExclude(string absolutePath) {
        return ProjectFileScanner.ShouldExclude(absolutePath, _options!.ProjectRootPath, _ignoreEngine);
    }

    /// <summary>
    /// Analyzes the timeline log to determine the last recorded state of the file watcher before the agent started.
    /// </summary>
    /// <returns>A string representing the previous watcher state.</returns>
    /// <param name="timelinePath">The file path to the timeline log.</param>
    /// <param name="wasInterrupted">A flag indicating if the agent was flagged as interrupted during preparation.</param>
    private static string DeterminePreviousWatcherState(string timelinePath, bool wasInterrupted) {
        if (wasInterrupted) {
            return "Interrupted";
        }

        if (!File.Exists(timelinePath)) {
            return "Unknown";
        }

        try {
            var lines = File.ReadAllLines(timelinePath);
            for (var i = lines.Length - 1; i >= 0; i--) {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var owsEvent = JsonSerializer.Deserialize<OwsEvent>(lines[i]);
                if (owsEvent != null) {
                    if (owsEvent.EventType == OwsEventType.WatcherStopped) {
                        return "CleanStopped";
                    }

                    if (owsEvent.EventType == OwsEventType.WatcherInterrupted) {
                        return "Interrupted";
                    }

                    if (owsEvent.EventType == OwsEventType.WatcherStarted ||
                        owsEvent.EventType == OwsEventType.WatcherRecovered) {
                        return "Unknown";
                    }
                }
            }
        } catch {
            // Ignore and return default
        }

        return "CleanStopped";
    }
}
