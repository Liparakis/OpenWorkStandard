using System.Text.Json;
using Ows.Core.Events;

namespace Ows.Core.Agent.Timeline;

/// <summary>
///     Provides utility methods to read and append chained events to local timeline log files.
/// </summary>
internal static class TimelineEventAppender {
    /// <summary>
    ///     Reads the head event hash of the existing timeline file.
    /// </summary>
    /// <param name="timelinePath">The absolute path to the timeline file.</param>
    /// <returns>The last event's hash, or the genesis previous hash if the timeline is empty or missing.</returns>
    public static string ReadLastEventHash(string timelinePath) {
        return File.Exists(timelinePath)
            ? OwsEventChain.ReadLastEventHash(timelinePath)
            : OwsEventChain.GenesisPreviousEventHash;
    }

    /// <summary>
    ///     Appends a new event to the timeline file, establishing parent-child hash chaining.
    /// </summary>
    /// <param name="timelinePath">The absolute path to the timeline file.</param>
    /// <param name="owsEvent">The raw event payload details to append.</param>
    /// <param name="previousEventHash">The hash of the previous head event.</param>
    /// <param name="cancellationToken">Token to cancel the write operation.</param>
    /// <returns>A task returning the new chained head event hash.</returns>
    public static async Task<string> AppendEventAsync(
        string timelinePath,
        OwsEvent owsEvent,
        string previousEventHash,
        CancellationToken cancellationToken
    ) {
        var localFolder = Path.GetDirectoryName(timelinePath);
        if (!string.IsNullOrEmpty(localFolder)) {
            Directory.CreateDirectory(localFolder);
        }

        var chainedEvent = OwsEventChain.CreateChainedEvent(owsEvent, previousEventHash);
        var serialized = JsonSerializer.Serialize(chainedEvent) + Environment.NewLine;
        await File.AppendAllTextAsync(timelinePath, serialized, cancellationToken);
        return chainedEvent.EventHash;
    }
}
