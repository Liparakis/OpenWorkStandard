using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Ows.Core.Events;
using Ows.Core.Hashing;

namespace Ows.Core.Agent;

/// <summary>
/// Provides the local tracking agent that performs a project scan (baseline or recovery) and then
/// watches for file-system changes, appending chained provenance events to the timeline.
/// </summary>
public sealed class LocalTrackingAgent(ILogger<LocalTrackingAgent> logger) : ITrackingAgent
{
    private TrackingAgentOptions? _options;
    private readonly SemaphoreSlim _timelineLock = new(1, 1);
    private ObservedSnapshot _currentSnapshot = new();

    private static readonly JsonSerializerOptions SnapshotSerializerOptions = new() { WriteIndented = true };

    private static readonly string[] DefaultExclusions =
    [
        ".ows",
        ".git",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "build",
        ".vs",
        "target",
        "coverage"
    ];

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

        // Run recovery scan
        await PerformRecoveryScanAsync(timelinePath, projectId, cancellationToken);
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

    private async Task PerformRecoveryScanAsync(string timelinePath, string projectId, CancellationToken cancellationToken)
    {
        var localFolder = Path.Combine(_options!.ProjectRootPath, OwsConstants.LocalFolderName);
        var snapshotPath = Path.Combine(localFolder, OwsConstants.ObservedSnapshotFileName);
        var hashService = new Sha256HashService();

        var currentFiles = ScanCurrentFiles(hashService);

        ObservedSnapshot? previousSnapshot = null;
        string? computedSnapshotHash = null;
        var hadSnapshotFile = File.Exists(snapshotPath);
        var snapshotUnreadable = false;
        if (File.Exists(snapshotPath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(snapshotPath, cancellationToken);
                previousSnapshot = JsonSerializer.Deserialize<ObservedSnapshot>(content);
                if (previousSnapshot != null)
                {
                    computedSnapshotHash = SnapshotHashCalculator.ComputeHash(previousSnapshot);
                }
            }
            catch (Exception ex)
            {
                snapshotUnreadable = true;
                logger.LogWarning("Failed to parse observed snapshot: {Message}. Treating as corrupted and running clean scan.", ex.Message);
                try { File.Delete(snapshotPath); } catch { /*ignored*/ }
            }
        }

        await _timelineLock.WaitAsync(cancellationToken);
        try
        {
            var previousEventHash = File.Exists(timelinePath)
                ? OwsEventChain.ReadLastEventHash(timelinePath)
                : OwsEventChain.GenesisPreviousEventHash;
            var hadPriorTimeline = File.Exists(timelinePath) &&
                                   File.ReadLines(timelinePath).Any(line => !string.IsNullOrWhiteSpace(line));
            var committedSnapshotHash = hadPriorTimeline ? FindLatestCommittedSnapshotHash(timelinePath) : null;
            var snapshotHashMismatch = previousSnapshot != null &&
                                       !string.IsNullOrWhiteSpace(committedSnapshotHash) &&
                                       !string.Equals(computedSnapshotHash, committedSnapshotHash, StringComparison.OrdinalIgnoreCase);
            var snapshotLegacyUnbound = previousSnapshot != null &&
                                        hadPriorTimeline &&
                                        string.IsNullOrWhiteSpace(committedSnapshotHash);

            if (previousSnapshot == null && hadPriorTimeline)
            {
                var now = DateTimeOffset.UtcNow;
                var lastHeartbeat = GetLastHeartbeatTime(_options.ProjectRootPath);
                var gapStartedAt = lastHeartbeat ?? now;
                var gapDurationMsVal = (long)(now - gapStartedAt).TotalMilliseconds;
                var previousStateVal = DeterminePreviousWatcherState(timelinePath, _options.WasInterrupted);

                if (_options.WasInterrupted)
                {
                    var interruptedEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
                    {
                        EventType = OwsEventType.WatcherInterrupted,
                        ProjectId = projectId,
                        ToolName = "ows watch",
                        Metadata = new Dictionary<string, string>
                        {
                            { "previousPid", _options.InterruptedState?.Pid.ToString() ?? "unknown" },
                            { "reason", "stale_pid" }
                        }
                    }, previousEventHash);

                    await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(interruptedEvent)}{Environment.NewLine}", cancellationToken);
                    previousEventHash = interruptedEvent.EventHash;

                    var recoveredEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
                    {
                        EventType = OwsEventType.WatcherRecovered,
                        ProjectId = projectId,
                        ToolName = "ows watch",
                        Metadata = new Dictionary<string, string>
                        {
                            { "reason", "crash_recovery" },
                            { "gapDurationMs", gapDurationMsVal.ToString() }
                        }
                    }, previousEventHash);

                    await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(recoveredEvent)}{Environment.NewLine}", cancellationToken);
                    previousEventHash = recoveredEvent.EventHash;
                }
                else
                {
                    var startedEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
                    {
                        EventType = OwsEventType.WatcherStarted,
                        ProjectId = projectId,
                        ToolName = "ows watch",
                        Metadata = new Dictionary<string, string>
                        {
                            { "reason", "user_restart" }
                        }
                    }, previousEventHash);

                    await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(startedEvent)}{Environment.NewLine}", cancellationToken);
                    previousEventHash = startedEvent.EventHash;
                }

                var gapEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
                {
                    EventType = OwsEventType.ObservationGapDetected,
                    ProjectId = projectId,
                    ToolName = "ows watch",
                    Metadata = new Dictionary<string, string>
                    {
                        { "gapStartedAt", gapStartedAt.ToString("o") },
                        { "gapEndedAt", now.ToString("o") },
                        { "gapDurationMs", gapDurationMsVal.ToString() },
                        { "previousState", previousStateVal },
                        { "recoveryReason", _options.WasInterrupted ? "crash_recovery" : "user_start" },
                        { "baselineState", snapshotUnreadable ? "corrupt_snapshot" : hadSnapshotFile ? "missing_snapshot" : "missing_snapshot" }
                    }
                }, previousEventHash);

                await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(gapEvent)}{Environment.NewLine}", cancellationToken);
                previousEventHash = gapEvent.EventHash;
            }
            else if (snapshotHashMismatch || snapshotLegacyUnbound)
            {
                var now = DateTimeOffset.UtcNow;
                var lastHeartbeat = GetLastHeartbeatTime(_options.ProjectRootPath);
                var gapStartedAt = lastHeartbeat ?? now;
                var gapDurationMsVal = (long)(now - gapStartedAt).TotalMilliseconds;
                var previousStateVal = DeterminePreviousWatcherState(timelinePath, _options.WasInterrupted);

                previousEventHash = await AppendRecoveryLifecycleEventsAsync(
                    timelinePath,
                    projectId,
                    previousEventHash,
                    gapDurationMsVal,
                    cancellationToken);

                var baselineState = snapshotHashMismatch ? "snapshot_hash_mismatch" : "legacy_unbound_snapshot";
                var gapMetadata = new Dictionary<string, string>
                {
                    { "gapStartedAt", gapStartedAt.ToString("o") },
                    { "gapEndedAt", now.ToString("o") },
                    { "gapDurationMs", gapDurationMsVal.ToString() },
                    { "previousState", previousStateVal },
                    { "recoveryReason", _options.WasInterrupted ? "crash_recovery" : "user_start" },
                    { "baselineState", baselineState }
                };
                if (!string.IsNullOrWhiteSpace(committedSnapshotHash))
                {
                    gapMetadata["committedSnapshotHash"] = committedSnapshotHash;
                }
                if (!string.IsNullOrWhiteSpace(computedSnapshotHash))
                {
                    gapMetadata["computedSnapshotHash"] = computedSnapshotHash;
                }

                var gapEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
                {
                    EventType = OwsEventType.ObservationGapDetected,
                    ProjectId = projectId,
                    ToolName = "ows watch",
                    Metadata = gapMetadata
                }, previousEventHash);

                await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(gapEvent)}{Environment.NewLine}", cancellationToken);
                previousEventHash = gapEvent.EventHash;
            }
            else if (previousSnapshot == null)
            {
                // 1. Initial/first run: Emit file creation baseline events and WatcherStarted
                foreach (var fileState in currentFiles.Values)
                {
                    var owsEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
                    {
                        EventType = OwsEventType.FileCreated,
                        ProjectId = projectId,
                        RelativePath = fileState.RelativePath,
                        ToolName = "ows watch",
                        BytesChanged = fileState.Size,
                        Metadata = new Dictionary<string, string>
                        {
                            { "source", "initial_baseline" },
                            { "usedForTrust", "false" }
                        }
                    }, previousEventHash);

                    await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(owsEvent)}{Environment.NewLine}", cancellationToken);
                    previousEventHash = owsEvent.EventHash;
                }

                var startedEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
                {
                    EventType = OwsEventType.WatcherStarted,
                    ProjectId = projectId,
                    ToolName = "ows watch",
                    Metadata = new Dictionary<string, string>
                    {
                        { "reason", "user_start" }
                    }
                }, previousEventHash);

                await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(startedEvent)}{Environment.NewLine}", cancellationToken);
                previousEventHash = startedEvent.EventHash;
            }
            else
            {
                // 2. Resume / recovery scan
                var now = DateTimeOffset.UtcNow;

                // Gap computation parameters
                var lastHeartbeat = GetLastHeartbeatTime(_options.ProjectRootPath);
                var gapStartedAt = previousSnapshot.ObservedAt;
                if (lastHeartbeat.HasValue && lastHeartbeat.Value > gapStartedAt)
                {
                    gapStartedAt = lastHeartbeat.Value;
                }
                var gapEndedAt = now;
                var gapDurationMsVal = (long)(gapEndedAt - gapStartedAt).TotalMilliseconds;

                var previousStateVal = DeterminePreviousWatcherState(timelinePath, _options.WasInterrupted);

                previousEventHash = await AppendRecoveryLifecycleEventsAsync(
                    timelinePath,
                    projectId,
                    previousEventHash,
                    gapDurationMsVal,
                    cancellationToken);

                // Compare snapshot to current state
                var createdFiles = new List<ObservedFileState>();
                var modifiedFiles = new List<(ObservedFileState Prev, ObservedFileState Curr)>();
                var deletedFiles = new List<ObservedFileState>();

                foreach (var file in currentFiles)
                {
                    if (!previousSnapshot.Files.TryGetValue(file.Key, out var prev))
                    {
                        createdFiles.Add(file.Value);
                    }
                    else if (!string.Equals(prev.FileHash, file.Value.FileHash, StringComparison.Ordinal) || prev.Size != file.Value.Size)
                    {
                        modifiedFiles.Add((prev, file.Value));
                    }
                }

                foreach (var file in previousSnapshot.Files)
                {
                    if (!currentFiles.ContainsKey(file.Key))
                    {
                        deletedFiles.Add(file.Value);
                    }
                }

                bool hasDifferences = createdFiles.Count > 0 || modifiedFiles.Count > 0 || deletedFiles.Count > 0;
                if (hasDifferences)
                {
                    // Emit ObservationGapDetected
                    var gapEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
                    {
                        EventType = OwsEventType.ObservationGapDetected,
                        ProjectId = projectId,
                        ToolName = "ows watch",
                        Metadata = new Dictionary<string, string>
                        {
                            { "gapStartedAt", gapStartedAt.ToString("o") },
                            { "gapEndedAt", gapEndedAt.ToString("o") },
                            { "gapDurationMs", gapDurationMsVal.ToString() },
                            { "previousState", previousStateVal },
                            { "recoveryReason", _options.WasInterrupted ? "crash_recovery" : "user_start" }
                        }
                    }, previousEventHash);

                    await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(gapEvent)}{Environment.NewLine}", cancellationToken);
                    previousEventHash = gapEvent.EventHash;

                    // Emit change events
                    var largeEnabled = IsLargeUnobservedChangeEnabled();
                    var byteThreshold = GetLargeUnobservedChangeByteThreshold();
                    var lineThreshold = GetLargeUnobservedChangeLineThreshold();

                    // Created files
                    foreach (var file in createdFiles)
                    {
                        var isLarge = largeEnabled && (Math.Abs(file.Size) >= byteThreshold || Math.Abs(file.LineCount) >= lineThreshold);
                        var changeEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
                        {
                            EventType = isLarge ? OwsEventType.LargeUnobservedChangeDetected : OwsEventType.UnobservedChangeDetected,
                            ProjectId = projectId,
                            RelativePath = file.RelativePath,
                            ToolName = "ows watch",
                            BytesChanged = file.Size,
                            Metadata = new Dictionary<string, string>
                            {
                                { "changeKind", "Created" },
                                { "relativePath", file.RelativePath },
                                { "currentHash", file.FileHash },
                                { "currentSize", file.Size.ToString() },
                                { "bytesDelta", file.Size.ToString() },
                                { "lineDeltaEstimate", file.LineCount.ToString() },
                                { "gapDurationMs", gapDurationMsVal.ToString() }
                            }
                        }, previousEventHash);

                        await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(changeEvent)}{Environment.NewLine}", cancellationToken);
                        previousEventHash = changeEvent.EventHash;
                    }

                    // Modified files
                    foreach (var file in modifiedFiles)
                    {
                        var bytesDelta = file.Curr.Size - file.Prev.Size;
                        var lineDelta = file.Curr.LineCount - file.Prev.LineCount;
                        var isLarge = largeEnabled && (Math.Abs(bytesDelta) >= byteThreshold || Math.Abs(lineDelta) >= lineThreshold);

                        var changeEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
                        {
                            EventType = isLarge ? OwsEventType.LargeUnobservedChangeDetected : OwsEventType.UnobservedChangeDetected,
                            ProjectId = projectId,
                            RelativePath = file.Curr.RelativePath,
                            ToolName = "ows watch",
                            BytesChanged = bytesDelta,
                            Metadata = new Dictionary<string, string>
                            {
                                { "changeKind", "Modified" },
                                { "relativePath", file.Curr.RelativePath },
                                { "previousHash", file.Prev.FileHash },
                                { "currentHash", file.Curr.FileHash },
                                { "previousSize", file.Prev.Size.ToString() },
                                { "currentSize", file.Curr.Size.ToString() },
                                { "bytesDelta", bytesDelta.ToString() },
                                { "lineDeltaEstimate", lineDelta.ToString() },
                                { "gapDurationMs", gapDurationMsVal.ToString() }
                            }
                        }, previousEventHash);

                        await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(changeEvent)}{Environment.NewLine}", cancellationToken);
                        previousEventHash = changeEvent.EventHash;
                    }

                    // Deleted files
                    foreach (var file in deletedFiles)
                    {
                        var bytesDelta = -file.Size;
                        var lineDelta = -file.LineCount;
                        var isLarge = largeEnabled && (Math.Abs(bytesDelta) >= byteThreshold || Math.Abs(lineDelta) >= lineThreshold);

                        var changeEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
                        {
                            EventType = isLarge ? OwsEventType.LargeUnobservedChangeDetected : OwsEventType.UnobservedChangeDetected,
                            ProjectId = projectId,
                            RelativePath = file.RelativePath,
                            ToolName = "ows watch",
                            BytesChanged = bytesDelta,
                            Metadata = new Dictionary<string, string>
                            {
                                { "changeKind", "Deleted" },
                                { "relativePath", file.RelativePath },
                                { "previousHash", file.FileHash },
                                { "previousSize", file.Size.ToString() },
                                { "currentSize", "0" },
                                { "bytesDelta", bytesDelta.ToString() },
                                { "lineDeltaEstimate", lineDelta.ToString() },
                                { "gapDurationMs", gapDurationMsVal.ToString() }
                            }
                        }, previousEventHash);

                        await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(changeEvent)}{Environment.NewLine}", cancellationToken);
                        previousEventHash = changeEvent.EventHash;
                    }
                }
            }

            // Save the updated snapshot
            _currentSnapshot = new ObservedSnapshot
            {
                ObservedAt = DateTimeOffset.UtcNow,
                Files = currentFiles
            };
            await SaveSnapshotAtomicallyAsync(snapshotPath, _currentSnapshot, cancellationToken);
            await AppendSnapshotUpdatedEventAsync(
                timelinePath,
                projectId,
                previousEventHash,
                previousSnapshot == null ? "initial_baseline" : "recovery_scan",
                computedSnapshotHash,
                CancellationToken.None);
        }
        finally
        {
            _timelineLock.Release();
        }
    }

    private Dictionary<string, ObservedFileState> ScanCurrentFiles(Sha256HashService hashService)
    {
        var files = new Dictionary<string, ObservedFileState>(StringComparer.OrdinalIgnoreCase);
        var projectRoot = _options!.ProjectRootPath;

        var trackedFiles = Directory
            .EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
            .Where(path => !ShouldExclude(path))
            .ToList();

        var now = DateTimeOffset.UtcNow;
        foreach (var path in trackedFiles)
        {
            var relative = Path.GetRelativePath(projectRoot, path);
            try
            {
                var info = new FileInfo(path);
                var size = info.Length;
                var lastWrite = info.LastWriteTimeUtc;
                var lineCount = GetLineCountEstimate(path);
                var fileHash = GetFileHash(path, hashService);

                files[relative] = new ObservedFileState
                {
                    RelativePath = relative,
                    FileHash = fileHash,
                    Size = size,
                    LineCount = lineCount,
                    LastWriteTime = lastWrite,
                    ObservedAt = now
                };
            }
            catch (IOException)
            {
                // File locked or missing, skip
            }
        }

        return files;
    }

    private async Task<string> AppendRecoveryLifecycleEventsAsync(
        string timelinePath,
        string projectId,
        string previousEventHash,
        long gapDurationMsVal,
        CancellationToken cancellationToken)
    {
        if (_options!.WasInterrupted)
        {
            var interruptedEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
            {
                EventType = OwsEventType.WatcherInterrupted,
                ProjectId = projectId,
                ToolName = "ows watch",
                Metadata = new Dictionary<string, string>
                {
                    { "previousPid", _options.InterruptedState?.Pid.ToString() ?? "unknown" },
                    { "reason", "stale_pid" }
                }
            }, previousEventHash);

            await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(interruptedEvent)}{Environment.NewLine}", cancellationToken);
            previousEventHash = interruptedEvent.EventHash;

            var recoveredEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
            {
                EventType = OwsEventType.WatcherRecovered,
                ProjectId = projectId,
                ToolName = "ows watch",
                Metadata = new Dictionary<string, string>
                {
                    { "reason", "crash_recovery" },
                    { "gapDurationMs", gapDurationMsVal.ToString() }
                }
            }, previousEventHash);

            await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(recoveredEvent)}{Environment.NewLine}", cancellationToken);
            return recoveredEvent.EventHash;
        }

        var startedEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
        {
            EventType = OwsEventType.WatcherStarted,
            ProjectId = projectId,
            ToolName = "ows watch",
            Metadata = new Dictionary<string, string>
            {
                { "reason", "user_restart" }
            }
        }, previousEventHash);

        await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(startedEvent)}{Environment.NewLine}", cancellationToken);
        return startedEvent.EventHash;
    }

    private async Task AppendSnapshotUpdatedEventAsync(
        string timelinePath,
        string projectId,
        string previousEventHash,
        string reason,
        string? previousSnapshotHash,
        CancellationToken cancellationToken)
    {
        var snapshotHash = SnapshotHashCalculator.ComputeHash(_currentSnapshot);
        var metadata = new Dictionary<string, string>
        {
            { "snapshotHash", snapshotHash },
            { "fileCount", _currentSnapshot.Files.Count.ToString() },
            { "observedAt", _currentSnapshot.ObservedAt.ToUniversalTime().ToString("O") },
            { "reason", reason }
        };

        if (!string.IsNullOrWhiteSpace(previousSnapshotHash))
        {
            metadata["previousSnapshotHash"] = previousSnapshotHash;
        }

        var snapshotEvent = OwsEventChain.CreateChainedEvent(new OwsEvent
        {
            EventType = OwsEventType.SnapshotUpdated,
            ProjectId = projectId,
            ToolName = "ows watch",
            Metadata = metadata
        }, previousEventHash);

        await File.AppendAllTextAsync(timelinePath, $"{JsonSerializer.Serialize(snapshotEvent)}{Environment.NewLine}", cancellationToken);
    }

    private static string? FindLatestCommittedSnapshotHash(string timelinePath)
    {
        if (!File.Exists(timelinePath))
        {
            return null;
        }

        foreach (var line in File.ReadLines(timelinePath).Reverse())
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var owsEvent = JsonSerializer.Deserialize<OwsEvent>(line);
                if (owsEvent?.EventType == OwsEventType.SnapshotUpdated &&
                    owsEvent.Metadata.TryGetValue("snapshotHash", out var snapshotHash) &&
                    !string.IsNullOrWhiteSpace(snapshotHash))
                {
                    return snapshotHash;
                }
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private async Task SaveSnapshotAtomicallyAsync(string snapshotPath, ObservedSnapshot snapshot, CancellationToken cancellationToken)
    {
        var tempPath = snapshotPath + ".tmp";
        var directory = Path.GetDirectoryName(snapshotPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(snapshot, SnapshotSerializerOptions);

        await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        await using (var writer = new StreamWriter(fs, System.Text.Encoding.UTF8))
        {
            await writer.WriteAsync(json);
            await writer.FlushAsync(cancellationToken);
            await fs.FlushAsync(cancellationToken);
        }

        // Atomic move
        File.Move(tempPath, snapshotPath, overwrite: true);
    }

    private DateTimeOffset? GetLastHeartbeatTime(string projectRoot)
    {
        var sessionPath = Path.Combine(projectRoot, OwsConstants.LocalFolderName, OwsConstants.SessionFileName);
        if (File.Exists(sessionPath))
        {
            try
            {
                var content = File.ReadAllText(sessionPath);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("lastHeartbeatAt", out var prop) && prop.TryGetDateTimeOffset(out var dto))
                {
                    return dto;
                }
            }
            catch { /*ignored*/}
        }
        return null;
    }

    private static int GetLineCountEstimate(string path)
    {
        try
        {
            if (!File.Exists(path)) return 0;
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return 0;
            var count = 1;
            foreach (var t in bytes)
            {
                if (t == '\n') count++;
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    private static string GetFileHash(string path, Sha256HashService hashService)
    {
        try
        {
            if (!File.Exists(path)) return string.Empty;
            return hashService.ComputeHash(File.ReadAllBytes(path));
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool IsLargeUnobservedChangeEnabled()
    {
        var envVal = Environment.GetEnvironmentVariable("OwsCapture:LargeUnobservedChange:Enabled")
                     ?? Environment.GetEnvironmentVariable("OwsCapture__LargeUnobservedChange__Enabled")
                     ?? Environment.GetEnvironmentVariable("OWS_CAPTURE_LARGE_UNOBSERVED_CHANGE_ENABLED");
        if (bool.TryParse(envVal, out var enabled)) return enabled;
        return true;
    }

    private long GetLargeUnobservedChangeByteThreshold()
    {
        var envVal = Environment.GetEnvironmentVariable("OwsCapture:LargeUnobservedChange:ByteThreshold")
                     ?? Environment.GetEnvironmentVariable("OwsCapture__LargeUnobservedChange__ByteThreshold")
                     ?? Environment.GetEnvironmentVariable("OWS_CAPTURE_LARGE_UNOBSERVED_CHANGE_BYTETHRESHOLD");
        if (long.TryParse(envVal, out var threshold)) return threshold;
        return 50000;
    }

    private int GetLargeUnobservedChangeLineThreshold()
    {
        var envVal = Environment.GetEnvironmentVariable("OwsCapture:LargeUnobservedChange:LineThreshold")
                     ?? Environment.GetEnvironmentVariable("OwsCapture__LargeUnobservedChange__LineThreshold")
                     ?? Environment.GetEnvironmentVariable("OWS_CAPTURE_LARGE_UNOBSERVED_CHANGE_LINETHRESHOLD");
        if (int.TryParse(envVal, out var threshold)) return threshold;
        return 300;
    }

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
        var absolutePath = Path.Combine(_options!.ProjectRootPath, watchEvent.RelativePath);
        if (watchEvent.ChangeKind != FileChangeKind.Deleted)
        {
            try
            {
                bytesChanged = new FileInfo(absolutePath).Length;
            }
            catch (IOException)
            {
            }
        }

        await _timelineLock.WaitAsync(cancellationToken);
        try
        {
            var previousEventHash = OwsEventChain.ReadLastEventHash(timelinePath);
            var previousSnapshotHash = SnapshotHashCalculator.ComputeHash(_currentSnapshot);
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

            // Update snapshot
            var hashService = new Sha256HashService();
            if (watchEvent.ChangeKind == FileChangeKind.Deleted)
            {
                _currentSnapshot.Files.Remove(watchEvent.RelativePath);
            }
            else
            {
                try
                {
                    if (File.Exists(absolutePath))
                    {
                        var info = new FileInfo(absolutePath);
                        _currentSnapshot.Files[watchEvent.RelativePath] = new ObservedFileState
                        {
                            RelativePath = watchEvent.RelativePath,
                            FileHash = GetFileHash(absolutePath, hashService),
                            Size = info.Length,
                            LineCount = GetLineCountEstimate(absolutePath),
                            LastWriteTime = info.LastWriteTimeUtc,
                            ObservedAt = DateTimeOffset.UtcNow
                        };
                    }
                }
                catch { /*ignored*/ }
            }
            _currentSnapshot.ObservedAt = DateTimeOffset.UtcNow;
            var localFolder = Path.Combine(_options!.ProjectRootPath, OwsConstants.LocalFolderName);
            var snapshotPath = Path.Combine(localFolder, OwsConstants.ObservedSnapshotFileName);
            await SaveSnapshotAtomicallyAsync(snapshotPath, _currentSnapshot, cancellationToken);
            await AppendSnapshotUpdatedEventAsync(
                timelinePath,
                projectId,
                owsEvent.EventHash,
                "watcher_event",
                previousSnapshotHash,
                CancellationToken.None);
        }
        finally
        {
            _timelineLock.Release();
        }
    }

    private bool ShouldExclude(string absolutePath)
    {
        // Check local .ows folder
        if (absolutePath.Contains($"{Path.DirectorySeparatorChar}{OwsConstants.LocalFolderName}{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            absolutePath.Contains($"/{OwsConstants.LocalFolderName}/", StringComparison.Ordinal))
        {
            return true;
        }

        // Parse path segments to check exclusions
        var relative = Path.GetRelativePath(_options!.ProjectRootPath, absolutePath);
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        // Get exclusion list
        var exclusions = new List<string>(DefaultExclusions);
        if (_options?.WatcherOptions.ExcludeDirectories != null)
        {
            exclusions.AddRange(_options.WatcherOptions.ExcludeDirectories);
        }

        foreach (var segment in segments)
        {
            foreach (var exclusion in exclusions)
            {
                if (string.Equals(segment, exclusion, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private string DeterminePreviousWatcherState(string timelinePath, bool wasInterrupted)
    {
        if (wasInterrupted)
        {
            return "Interrupted";
        }

        if (!File.Exists(timelinePath))
        {
            return "Unknown";
        }

        try
        {
            var lines = File.ReadAllLines(timelinePath);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var owsEvent = JsonSerializer.Deserialize<OwsEvent>(lines[i]);
                if (owsEvent != null)
                {
                    if (owsEvent.EventType == OwsEventType.WatcherStopped)
                    {
                        return "CleanStopped";
                    }
                    if (owsEvent.EventType == OwsEventType.WatcherInterrupted)
                    {
                        return "Interrupted";
                    }
                    if (owsEvent.EventType == OwsEventType.WatcherStarted || owsEvent.EventType == OwsEventType.WatcherRecovered)
                    {
                        return "Unknown";
                    }
                }
            }
        }
        catch
        {
            // Ignore and return default
        }

        return "CleanStopped";
    }
}
