using System.IO.Compression;
using System.Text.Json;
using Ows.Core.Events;

namespace Ows.Core.Verification;

/// <summary>
/// Analyzes the local timeline event log for indicators of watcher observation gaps and unobserved changes.
/// </summary>
internal static class ObservationContinuityAnalyzer {
    /// <summary>
    /// Scans the timeline from the packaged archive to build appropriate findings on observation gaps, unobserved changes, and legacy/mismatched snapshot states.
    /// </summary>
    /// <param name="archive">The ZIP archive package containing project files and history.</param>
    /// <param name="findings">The list of findings to append newly discovered anomalies or validations to.</param>
    /// <param name="sawObservationGap">Out parameter indicating if any observation gaps were encountered.</param>
    /// <param name="sawLargeUnobservedChange">Out parameter indicating if any large unobserved changes were encountered.</param>
    /// <param name="sawUnobservedChange">Out parameter indicating if any standard unobserved changes were encountered.</param>
    public static void AnalyzeTimelineContinuity(
        ZipArchive archive,
        List<VerificationFinding> findings,
        out bool sawObservationGap,
        out bool sawLargeUnobservedChange,
        out bool sawUnobservedChange) {
        sawObservationGap = false;
        sawLargeUnobservedChange = false;
        sawUnobservedChange = false;

        var entry = archive.GetEntry(OwsConstants.TimelineFileName);
        if (entry is null) return;

        try {
            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream);
            while (reader.ReadLine() is { } line) {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var owsEvent = JsonSerializer.Deserialize<OwsEvent>(line);
                if (owsEvent != null) {
                    if (owsEvent.EventType == OwsEventType.ObservationGapDetected) {
                        sawObservationGap = true;
                        owsEvent.Metadata.TryGetValue("gapStartedAt", out var startStr);
                        owsEvent.Metadata.TryGetValue("gapEndedAt", out var endStr);
                        owsEvent.Metadata.TryGetValue("gapDurationMs", out var gapMsStr);
                        owsEvent.Metadata.TryGetValue("previousState", out var prevState);
                        owsEvent.Metadata.TryGetValue("baselineState", out var baselineState);

                        long.TryParse(gapMsStr, out var gapMs);
                        var gapDurationText = gapMs > 0 ? $"{gapMs / 1000.0:F1} seconds" : "unknown duration";

                        findings.Add(new VerificationFinding {
                            Code = "observation.gap",
                            Severity = "Low",
                            Title = "Observation gap detected",
                            Detail =
                                $"OWS was not observing the project for an interval of {gapDurationText} (Previous state: {prevState ?? "Unknown"}).",
                            TechnicalDetail =
                                $"Gap Started: {startStr}, Gap Ended: {endStr}, Duration: {gapDurationText}, Previous state: {prevState}.",
                            ReviewerAction =
                                "Manual review recommended. Event presence is evidence of recorded activity. Event absence is not proof of misconduct."
                        });

                        if (string.Equals(baselineState, "legacy_unbound_snapshot", StringComparison.Ordinal)) {
                            findings.Add(VerificationFindingFactory.SnapshotUnboundFinding);
                        } else if (string.Equals(baselineState, "snapshot_hash_mismatch", StringComparison.Ordinal) ||
                                   string.Equals(baselineState, "corrupt_snapshot", StringComparison.Ordinal) ||
                                    string.Equals(baselineState, "missing_snapshot", StringComparison.Ordinal)) {
                            findings.Add(VerificationFindingFactory.SnapshotMismatchFinding);
                        }
                    } else if (owsEvent.EventType == OwsEventType.UnobservedChangeDetected) {
                        sawUnobservedChange = true;
                        owsEvent.Metadata.TryGetValue("relativePath", out var relPath);
                        owsEvent.Metadata.TryGetValue("gapDurationMs", out var gapMsStr);
                        owsEvent.Metadata.TryGetValue("bytesDelta", out var bytesDeltaStr);
                        owsEvent.Metadata.TryGetValue("lineDeltaEstimate", out var linesStr);
                        owsEvent.Metadata.TryGetValue("changeKind", out var kind);

                        long.TryParse(gapMsStr, out var gapMs);
                        var gapDurationText = gapMs > 0 ? $"{gapMs / 1000.0:F1} seconds" : "unknown duration";

                        findings.Add(new VerificationFinding {
                            Code = "observation.unobserved_change",
                            Severity = "Medium",
                            Title = $"Unobserved file change in {relPath ?? "unknown file"}",
                            Detail =
                                $"A file change ({kind ?? "Modified"}: {bytesDeltaStr ?? "0"} bytes, {linesStr ?? "0"} lines) appeared while OWS was not observing this project.",
                            TechnicalDetail =
                                $"File: {relPath}, Change Kind: {kind}, Bytes Delta: {bytesDeltaStr}, Line Delta Estimate: {linesStr}, Gap Duration: {gapDurationText}.",
                            ReviewerAction =
                                "OWS was not observing this project during the interval below. During that interval, file changes appeared. OWS can verify the current package hashes, but cannot verify the unobserved edit process. This is not proof of misconduct. Reviewers should ask the student to explain this interval."
                        });
                    } else if (owsEvent.EventType == OwsEventType.LargeUnobservedChangeDetected) {
                        sawLargeUnobservedChange = true;
                        owsEvent.Metadata.TryGetValue("relativePath", out var relPath);
                        owsEvent.Metadata.TryGetValue("gapDurationMs", out var gapMsStr);
                        owsEvent.Metadata.TryGetValue("bytesDelta", out var bytesDeltaStr);
                        owsEvent.Metadata.TryGetValue("lineDeltaEstimate", out var linesStr);
                        owsEvent.Metadata.TryGetValue("changeKind", out var kind);

                        long.TryParse(gapMsStr, out var gapMs);
                        var gapDurationText = gapMs > 0 ? $"{gapMs / 1000.0:F1} seconds" : "unknown duration";

                        findings.Add(new VerificationFinding {
                            Code = "observation.large_unobserved_change",
                            Severity = "High",
                            Title = $"Large unobserved change in {relPath ?? "unknown file"}",
                            Detail =
                                $"A large file change ({kind ?? "Modified"}: {bytesDeltaStr ?? "0"} bytes, {linesStr ?? "0"} lines) appeared while OWS was not observing this project.",
                            TechnicalDetail =
                                $"File: {relPath}, Change Kind: {kind}, Bytes Delta: {bytesDeltaStr}, Line Delta Estimate: {linesStr}, Gap Duration: {gapDurationText}.",
                            ReviewerAction =
                                "OWS was not observing this project during the interval below. During that interval, a large file change appeared. OWS can verify the current package hashes, but cannot verify the unobserved edit process. OWS cannot determine the cause. This is not proof of misconduct. Reviewers should ask the student to explain this interval."
                        });
                    }
                }
            }
        } catch {
            /*ignore*/
        }
    }
}
