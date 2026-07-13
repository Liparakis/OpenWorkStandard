using Ows.Core.Events;

namespace Ows.Core.Agent.Watcher;

/// <summary>
///     Provides factory methods to construct timeline watcher events with their appropriate metadata.
/// </summary>
internal static class WatcherLifecycleEventBuilder {
    /// <summary>
    ///     Builds a WatcherStarted event.
    /// </summary>
    /// <param name="projectId">The identifier of the project.</param>
    /// <param name="reason">The startup reason.</param>
    /// <returns>A new <see cref="OwsEvent" /> representing the event.</returns>
    public static OwsEvent BuildWatcherStarted(string projectId, string reason) {
        return new OwsEvent {
            EventType = OwsEventType.WatcherStarted,
            ProjectId = projectId,
            ToolName = "OWS Agent",
            Metadata = new Dictionary<string, string> {
                { "reason", reason }
            }
        };
    }

    /// <summary>
    ///     Builds a WatcherInterrupted event.
    /// </summary>
    /// <param name="projectId">The identifier of the project.</param>
    /// <param name="previousPid">The process identifier that was interrupted.</param>
    /// <param name="reason">The interruption reason.</param>
    /// <returns>A new <see cref="OwsEvent" /> representing the event.</returns>
    public static OwsEvent BuildWatcherInterrupted(string projectId, string? previousPid, string reason) {
        return new OwsEvent {
            EventType = OwsEventType.WatcherInterrupted,
            ProjectId = projectId,
            ToolName = "OWS Agent",
            Metadata = new Dictionary<string, string> {
                { "previousPid", previousPid ?? "unknown" },
                { "reason", reason }
            }
        };
    }

    /// <summary>
    ///     Builds a WatcherRecovered event.
    /// </summary>
    /// <param name="projectId">The identifier of the project.</param>
    /// <param name="reason">The recovery reason.</param>
    /// <param name="gapDurationMs">The duration of the recovery interval in milliseconds.</param>
    /// <returns>A new <see cref="OwsEvent" /> representing the event.</returns>
    public static OwsEvent BuildWatcherRecovered(string projectId, string reason, long gapDurationMs) {
        return new OwsEvent {
            EventType = OwsEventType.WatcherRecovered,
            ProjectId = projectId,
            ToolName = "OWS Agent",
            Metadata = new Dictionary<string, string> {
                { "reason", reason },
                { "gapDurationMs", gapDurationMs.ToString() }
            }
        };
    }

    /// <summary>
    ///     Builds an ObservationGapDetected event.
    /// </summary>
    /// <param name="projectId">The identifier of the project.</param>
    /// <param name="gapStartedAt">The timestamp indicating when the gap started.</param>
    /// <param name="gapEndedAt">The timestamp indicating when the gap ended.</param>
    /// <param name="gapDurationMs">The gap duration in milliseconds.</param>
    /// <param name="previousState">The state of the watcher prior to the gap.</param>
    /// <param name="recoveryReason">The reason for recovery start.</param>
    /// <param name="baselineState">The baseline snapshot verification status.</param>
    /// <param name="committedSnapshotHash">Optional snapshot hash committed in prior timeline events.</param>
    /// <param name="computedSnapshotHash">Optional snapshot hash computed locally.</param>
    /// <returns>A new <see cref="OwsEvent" /> representing the gap event.</returns>
    public static OwsEvent BuildObservationGapDetected(
        string projectId,
        DateTimeOffset gapStartedAt,
        DateTimeOffset gapEndedAt,
        long gapDurationMs,
        string previousState,
        string recoveryReason,
        string baselineState,
        string? committedSnapshotHash = null,
        string? computedSnapshotHash = null
    ) {
        var metadata = new Dictionary<string, string> {
            { "gapStartedAt", gapStartedAt.ToString("o") },
            { "gapEndedAt", gapEndedAt.ToString("o") },
            { "gapDurationMs", gapDurationMs.ToString() },
            { "previousState", previousState },
            { "recoveryReason", recoveryReason },
            { "baselineState", baselineState }
        };

        if (!string.IsNullOrWhiteSpace(committedSnapshotHash)) {
            metadata["committedSnapshotHash"] = committedSnapshotHash;
        }

        if (!string.IsNullOrWhiteSpace(computedSnapshotHash)) {
            metadata["computedSnapshotHash"] = computedSnapshotHash;
        }

        return new OwsEvent {
            EventType = OwsEventType.ObservationGapDetected,
            ProjectId = projectId,
            ToolName = "OWS Agent",
            Metadata = metadata
        };
    }

    /// <summary>
    ///     Builds a SnapshotUpdated event.
    /// </summary>
    /// <param name="projectId">The identifier of the project.</param>
    /// <param name="snapshotHash">The SHA-256 hash of the updated snapshot.</param>
    /// <param name="fileCount">The number of files tracked in the snapshot.</param>
    /// <param name="observedAt">The timestamp when the snapshot was taken.</param>
    /// <param name="reason">The reason for the update.</param>
    /// <param name="previousSnapshotHash">Optional prior snapshot hash.</param>
    /// <returns>A new <see cref="OwsEvent" /> representing the update event.</returns>
    public static OwsEvent BuildSnapshotUpdated(
        string projectId,
        string snapshotHash,
        int fileCount,
        DateTimeOffset observedAt,
        string reason,
        string? previousSnapshotHash
    ) {
        var metadata = new Dictionary<string, string> {
            { "snapshotHash", snapshotHash },
            { "fileCount", fileCount.ToString() },
            { "observedAt", observedAt.ToUniversalTime().ToString("O") },
            { "reason", reason }
        };

        if (!string.IsNullOrWhiteSpace(previousSnapshotHash)) {
            metadata["previousSnapshotHash"] = previousSnapshotHash;
        }

        return new OwsEvent {
            EventType = OwsEventType.SnapshotUpdated,
            ProjectId = projectId,
            ToolName = "OWS Agent",
            Metadata = metadata
        };
    }
}
