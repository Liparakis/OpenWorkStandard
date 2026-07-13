using Ows.Core.Events;

namespace Ows.Core.Verification;

/// <summary>
///     Calculates local, deterministic activity-pattern review signals from validated OWS events.
/// </summary>
public static class OwsActivityPatternAnalyzer {
    /// <summary>
    ///     The minimum number of events required for a burst signal.
    /// </summary>
    private const int MinimumBurstEvents = 10;

    /// <summary>
    ///     The minimum number of files required for a bulk-creation signal.
    /// </summary>
    private const int MinimumBulkFiles = 5;

    /// <summary>
    ///     The minimum number of files required for compressed-duration and revision-density signals.
    /// </summary>
    private const int MinimumCompressedFiles = 5;

    /// <summary>
    ///     The minimum share of files created in one window for a bulk-creation signal.
    /// </summary>
    private const double BulkCreationShare = 0.75;

    /// <summary>
    ///     The maximum revision share for a low-revision-density signal.
    /// </summary>
    private const double LowRevisionShare = 0.25;

    /// <summary>
    ///     The fraction of the activity span after which a rewrite is considered late.
    /// </summary>
    private const double LateRewritePosition = 0.8;

    /// <summary>
    ///     The minimum changed-byte estimate for a late rewrite signal.
    /// </summary>
    private const long LargeRewriteBytes = 8_192;

    /// <summary>
    ///     The reviewer action shared by non-authoritative activity findings.
    /// </summary>
    private const string ReviewAction =
        "Review the activity interval and ask the author to explain it. This is not proof of misconduct or AI use.";

    /// <summary>
    ///     The maximum interval used to find concentrated event activity.
    /// </summary>
    private static readonly TimeSpan BurstWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     The maximum interval used to find concentrated file creation.
    /// </summary>
    private static readonly TimeSpan BulkCreationWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     The maximum activity span considered compressed for a multi-file project.
    /// </summary>
    private static readonly TimeSpan CompressedDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Analyzes validated project file events for concentrated or replacement-style activity.
    /// </summary>
    /// <param name="events">The validated OWS events to analyze.</param>
    /// <returns>Activity metrics and non-authoritative review findings.</returns>
    public static ActivityPatternAnalysis Analyze(IReadOnlyList<OwsEvent> events) {
        ArgumentNullException.ThrowIfNull(events);

        var meaningfulEvents = events.Where(IsMeaningfulFileEvent)
                                     .OrderBy(owsEvent => owsEvent.TimestampUtc)
                                     .ToArray();
        if (meaningfulEvents.Length == 0) {
            return new ActivityPatternAnalysis();
        }

        var paths = meaningfulEvents.Where(owsEvent => !string.IsNullOrWhiteSpace(owsEvent.RelativePath))
                                    .GroupBy(owsEvent => owsEvent.RelativePath!, StringComparer.OrdinalIgnoreCase)
                                    .ToArray();
        var distinctFileCount = paths.Length;
        var createdPaths = paths.Where(path => path.Any(owsEvent => owsEvent.EventType == OwsEventType.FileCreated))
                                .Select(path => path.Key)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var revisedPaths = paths.Where(path => path.Any(IsRevisionEvent))
                                .Select(path => path.Key)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var firstActivity = meaningfulEvents[0].TimestampUtc;
        var lastActivity = meaningfulEvents[^1].TimestampUtc;
        var activitySpan = lastActivity - firstActivity;
        var largestBurst = FindLargestWindow(meaningfulEvents, BurstWindow, countDistinctPaths: false);
        var largestCreationWindow = FindLargestWindow(
            meaningfulEvents.Where(owsEvent => owsEvent.EventType == OwsEventType.FileCreated).ToArray(),
            BulkCreationWindow,
            countDistinctPaths: true
        );
        var info = new ActivityPatternInfo {
            MeaningfulEventCount = meaningfulEvents.Length,
            DistinctFileCount = distinctFileCount,
            CreatedFileCount = createdPaths.Count,
            RevisedFileCount = revisedPaths.Count,
            FirstActivityUtc = firstActivity,
            LastActivityUtc = lastActivity,
            ActivitySpanSeconds = activitySpan.TotalSeconds,
            LargestBurstEventCount = largestBurst.Count,
            LargestBurstSpanSeconds = largestBurst.Span.TotalSeconds,
            LargestCreationWindowFileCount = largestCreationWindow.Count
        };
        var findings = new List<VerificationFinding>();

        if (largestBurst.Count >= MinimumBurstEvents) {
            findings.Add(
                new VerificationFinding {
                    Code = "activity.burst",
                    Severity = "Medium",
                    Title = "Concentrated activity burst",
                    Detail =
                        $"{largestBurst.Count} meaningful file events occurred within {FormatDuration(largestBurst.Span)}.",
                    TechnicalDetail =
                        $"Largest {BurstWindow.TotalSeconds:0}-second window contained {largestBurst.Count} of {meaningfulEvents.Length} meaningful file events.",
                    ReviewerAction = ReviewAction
                }
            );
        }

        if (createdPaths.Count >= MinimumBulkFiles &&
            largestCreationWindow.Count >= Math.Ceiling(createdPaths.Count * BulkCreationShare)) {
            findings.Add(
                new VerificationFinding {
                    Code = "activity.bulk_creation",
                    Severity = "Medium",
                    Title = "Bulk file creation",
                    Detail =
                        $"{largestCreationWindow.Count} of {createdPaths.Count} observed files were first created within {FormatDuration(largestCreationWindow.Span)}.",
                    TechnicalDetail =
                        $"The creation concentration window was {BulkCreationWindow.TotalSeconds:0} seconds and included {largestCreationWindow.Count} distinct paths.",
                    ReviewerAction = ReviewAction
                }
            );
        }

        if (distinctFileCount >= MinimumCompressedFiles && activitySpan <= CompressedDuration) {
            findings.Add(
                new VerificationFinding {
                    Code = "activity.compressed_duration",
                    Severity = "Medium",
                    Title = "Compressed activity duration",
                    Detail =
                        $"{distinctFileCount} files had meaningful activity across {FormatDuration(activitySpan)}.",
                    TechnicalDetail =
                        $"First meaningful event: {firstActivity:o}; last meaningful event: {lastActivity:o}.",
                    ReviewerAction = ReviewAction
                }
            );
        }

        if (distinctFileCount >= MinimumCompressedFiles &&
            revisedPaths.Count <= Math.Floor(distinctFileCount * LowRevisionShare)) {
            findings.Add(
                new VerificationFinding {
                    Code = "activity.low_revision_density",
                    Severity = "Low",
                    Title = "Low revision density",
                    Detail =
                        $"Only {revisedPaths.Count} of {distinctFileCount} observed files had more than an initial creation event.",
                    TechnicalDetail =
                        $"Revision density was {(double) revisedPaths.Count / distinctFileCount:P0}; the review threshold is {LowRevisionShare:P0} or lower.",
                    ReviewerAction = ReviewAction
                }
            );
        }

        if (HasLateRewrite(paths, firstActivity, activitySpan)) {
            findings.Add(
                new VerificationFinding {
                    Code = "activity.late_rewrite",
                    Severity = "Medium",
                    Title = "Large late file rewrite",
                    Detail = "A previously observed file received a large modification late in the activity span.",
                    TechnicalDetail =
                        $"Late rewrite threshold: final {(100 - (LateRewritePosition * 100)):0}% of the activity span and at least {LargeRewriteBytes:N0} changed bytes.",
                    ReviewerAction = ReviewAction
                }
            );
        }

        return new ActivityPatternAnalysis { Info = info, Findings = findings };
    }

    /// <summary>
    ///     Determines whether an event represents a meaningful project file operation.
    /// </summary>
    /// <param name="owsEvent">The event to classify.</param>
    /// <returns><see langword="true" /> for a meaningful non-metadata file event.</returns>
    private static bool IsMeaningfulFileEvent(OwsEvent owsEvent) {
        if (string.IsNullOrWhiteSpace(owsEvent.RelativePath) || IsOwsMetadataPath(owsEvent.RelativePath)) {
            return false;
        }

        if (owsEvent.Metadata.TryGetValue("usedForTrust", out var usedForTrust) &&
            string.Equals(usedForTrust, "false", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return owsEvent.EventType is OwsEventType.FileCreated or
            OwsEventType.FileModified or
            OwsEventType.FileDeleted or
            OwsEventType.LargeInsert;
    }

    /// <summary>
    ///     Determines whether an event represents a file revision.
    /// </summary>
    /// <param name="owsEvent">The event to classify.</param>
    /// <returns><see langword="true" /> for a modification or large insertion event.</returns>
    private static bool IsRevisionEvent(OwsEvent owsEvent) =>
        owsEvent.EventType is OwsEventType.FileModified or OwsEventType.LargeInsert;

    /// <summary>
    ///     Determines whether a relative path belongs to OWS metadata or package output.
    /// </summary>
    /// <param name="path">The relative project path.</param>
    /// <returns><see langword="true" /> when the path should be excluded from activity analysis.</returns>
    private static bool IsOwsMetadataPath(string path) =>
        path.Equals(OwsConstants.LocalFolderName, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith($"{OwsConstants.LocalFolderName}/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith($"{OwsConstants.LocalFolderName}\\", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(OwsConstants.PackageExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Finds the densest event or creation window within the supplied interval.
    /// </summary>
    /// <param name="events">The chronologically ordered events.</param>
    /// <param name="window">The maximum window duration.</param>
    /// <param name="countDistinctPaths">Whether to count distinct paths instead of events.</param>
    /// <returns>The densest matching activity window.</returns>
    private static ActivityWindow FindLargestWindow(
        IReadOnlyList<OwsEvent> events,
        TimeSpan window,
        bool countDistinctPaths
    ) {
        if (events.Count == 0) {
            return new ActivityWindow(0, TimeSpan.Zero);
        }

        var largest = new ActivityWindow(0, TimeSpan.Zero);

        // ponytail: bounded O(n²) scan; switch to a two-pointer window only if large timelines make verification slow.
        for (var start = 0; start < events.Count; start++) {
            var end = start;
            while (end < events.Count &&
                   (events[end].TimestampUtc - events[start].TimestampUtc) <= window) {
                end++;
            }

            var candidates = events.Skip(start).Take((end - start)).ToArray();
            var count = countDistinctPaths
                ? candidates.Where(owsEvent => !string.IsNullOrWhiteSpace(owsEvent.RelativePath))
                            .Select(owsEvent => owsEvent.RelativePath!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Count()
                : candidates.Length;
            var span = candidates[^1].TimestampUtc - candidates[0].TimestampUtc;
            if (count > largest.Count || (count == largest.Count && span < largest.Span)) {
                largest = new ActivityWindow(count, span);
            }
        }

        return largest;
    }

    /// <summary>
    ///     Determines whether a previously observed file received a large late revision.
    /// </summary>
    /// <param name="paths">The grouped meaningful file events.</param>
    /// <param name="firstActivity">The first meaningful activity timestamp.</param>
    /// <param name="activitySpan">The total meaningful activity span.</param>
    /// <returns><see langword="true" /> when a late large revision is present.</returns>
    private static bool HasLateRewrite(
        IEnumerable<IGrouping<string, OwsEvent>> paths,
        DateTimeOffset firstActivity,
        TimeSpan activitySpan
    ) {
        if (activitySpan <= TimeSpan.Zero) {
            return false;
        }

        var lateThreshold = firstActivity + TimeSpan.FromTicks((long) (activitySpan.Ticks * LateRewritePosition));
        return paths.SelectMany(path => path.OrderBy(owsEvent => owsEvent.TimestampUtc).Skip(1))
                    .Any(owsEvent => IsRevisionEvent(owsEvent) &&
                                     owsEvent.TimestampUtc >= lateThreshold &&
                                     owsEvent.BytesChanged >= LargeRewriteBytes
                    );
    }

    /// <summary>
    ///     Formats a duration for a reviewer-facing finding.
    /// </summary>
    /// <param name="duration">The duration to format.</param>
    /// <returns>A compact duration string.</returns>
    private static string FormatDuration(TimeSpan duration) => duration.TotalSeconds < 60
        ? $"{duration.TotalSeconds:0.0} seconds"
        : $"{duration.TotalMinutes:0.0} minutes";

    /// <summary>
    ///     Represents the densest event window found during analysis.
    /// </summary>
    private sealed record ActivityWindow(int Count, TimeSpan Span);
}

/// <summary>
///     Contains the results of activity-pattern analysis.
/// </summary>
public sealed record ActivityPatternAnalysis {
    /// <summary>
    ///     Gets the measured activity metrics.
    /// </summary>
    public ActivityPatternInfo Info { get; init; } = new();

    /// <summary>
    ///     Gets non-authoritative activity review signals.
    /// </summary>
    public IReadOnlyList<VerificationFinding> Findings { get; init; } = [];
}

/// <summary>
///     Contains measurable activity metrics derived from validated file events.
/// </summary>
public sealed record ActivityPatternInfo {
    /// <summary>
    ///     Gets the number of meaningful file events analyzed.
    /// </summary>
    public int MeaningfulEventCount { get; init; }

    /// <summary>
    ///     Gets the number of distinct file paths analyzed.
    /// </summary>
    public int DistinctFileCount { get; init; }

    /// <summary>
    ///     Gets the number of distinct paths with a creation event.
    /// </summary>
    public int CreatedFileCount { get; init; }

    /// <summary>
    ///     Gets the number of distinct paths with a revision event.
    /// </summary>
    public int RevisedFileCount { get; init; }

    /// <summary>
    ///     Gets the timestamp of the first meaningful file event.
    /// </summary>
    public DateTimeOffset? FirstActivityUtc { get; init; }

    /// <summary>
    ///     Gets the timestamp of the last meaningful file event.
    /// </summary>
    public DateTimeOffset? LastActivityUtc { get; init; }

    /// <summary>
    ///     Gets the span between the first and last meaningful file events in seconds.
    /// </summary>
    public double ActivitySpanSeconds { get; init; }

    /// <summary>
    ///     Gets the number of events in the densest one-minute activity window.
    /// </summary>
    public int LargestBurstEventCount { get; init; }

    /// <summary>
    ///     Gets the span of the densest activity window in seconds.
    /// </summary>
    public double LargestBurstSpanSeconds { get; init; }

    /// <summary>
    ///     Gets the number of distinct file creations in the densest one-minute creation window.
    /// </summary>
    public int LargestCreationWindowFileCount { get; init; }
}
