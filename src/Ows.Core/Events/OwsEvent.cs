using System.Text.Json.Serialization;

namespace Ows.Core.Events;

/// <summary>
///     Represents a normalized provenance event captured for tracked work.
/// </summary>
public sealed record OwsEvent {
    /// <summary>
    ///     Gets the unique identifier for the event.
    /// </summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>
    ///     Gets the UTC timestamp when the event was recorded.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Gets the normalized event type.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OwsEventType EventType { get; init; }

    /// <summary>
    ///     Gets the logical project identifier.
    /// </summary>
    public string ProjectId { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the relative path associated with the event, when applicable.
    /// </summary>
    public string? RelativePath { get; init; }

    /// <summary>
    ///     Gets the tool name that produced or observed the event, when known.
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    ///     Gets the hash before the event, when applicable.
    /// </summary>
    public string? HashBefore { get; init; }

    /// <summary>
    ///     Gets the hash after the event, when applicable.
    /// </summary>
    public string? HashAfter { get; init; }

    /// <summary>
    ///     Gets the approximate byte count affected by the event, when available.
    /// </summary>
    public long? BytesChanged { get; init; }

    /// <summary>
    ///     Gets the previous event hash in the timeline chain.
    /// </summary>
    public string PreviousEventHash { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the hash of the canonical event content excluding this property.
    /// </summary>
    public string EventHash { get; init; } = string.Empty;

    /// <summary>
    ///     Gets event-specific structured metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
