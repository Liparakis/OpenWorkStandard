using System.Text.Json;
using System.Text.Json.Serialization;
using Ows.Core.Hashing;

namespace Ows.Core.Events;

/// <summary>
/// Provides canonical hashing helpers for the timeline event chain.
/// </summary>
public static class OwsEventChain {
    /// <summary>
    /// Gets the expected previous-hash value for the first event in a timeline.
    /// </summary>
    public const string GenesisPreviousEventHash = "";

    /// <summary>
    /// Serialization options configured for event chain serialization, including string-based enum converters.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Computes the canonical event hash excluding the <see cref="OwsEvent.EventHash"/> field itself.
    /// </summary>
    /// <param name="owsEvent">The event to hash.</param>
    /// <returns>The lower-case SHA-256 digest of the canonical event JSON.</returns>
    public static string ComputeEventHash(OwsEvent owsEvent) {
        ArgumentNullException.ThrowIfNull(owsEvent);

        var canonicalEvent = new {
            owsEvent.EventId,
            owsEvent.TimestampUtc,
            owsEvent.EventType,
            owsEvent.ProjectId,
            owsEvent.RelativePath,
            owsEvent.ToolName,
            owsEvent.HashBefore,
            owsEvent.HashAfter,
            owsEvent.BytesChanged,
            owsEvent.PreviousEventHash,
            Metadata = owsEvent.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                               .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };

        return new Sha256HashService().ComputeHash(JsonSerializer.Serialize(canonicalEvent, SerializerOptions));
    }

    /// <summary>
    /// Creates a chained event with populated previous and current hashes.
    /// </summary>
    /// <param name="owsEvent">The source event.</param>
    /// <param name="previousEventHash">The previous event hash, or the genesis value for the first event.</param>
    /// <returns>A new event with chain fields populated.</returns>
    public static OwsEvent CreateChainedEvent(OwsEvent owsEvent, string previousEventHash) {
        ArgumentNullException.ThrowIfNull(owsEvent);

        var eventWithPreviousHash = owsEvent with { PreviousEventHash = previousEventHash };
        return eventWithPreviousHash with { EventHash = ComputeEventHash(eventWithPreviousHash) };
    }

    /// <summary>
    /// Reads the current timeline head hash from a JSONL event stream.
    /// </summary>
    /// <param name="timelinePath">The path to the timeline file.</param>
    /// <returns>The last event hash, or the genesis value when the timeline is empty.</returns>
    public static string ReadLastEventHash(string timelinePath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(timelinePath);

        string? lastNonEmptyLine = null;
        foreach (var line in File.ReadLines(timelinePath)) {
            if (!string.IsNullOrWhiteSpace(line)) {
                lastNonEmptyLine = line;
            }
        }

        if (string.IsNullOrWhiteSpace(lastNonEmptyLine)) {
            return GenesisPreviousEventHash;
        }

        var lastEvent = JsonSerializer.Deserialize<OwsEvent>(lastNonEmptyLine, SerializerOptions)
                        ?? throw new JsonException("Timeline event deserialized to null.");

        return lastEvent.EventHash;
    }
}
