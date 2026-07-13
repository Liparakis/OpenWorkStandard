using System.Text.Json.Serialization;
using Ows.Core.Agent;

namespace Ows.Core.Init;

/// <summary>
/// Represents the project-level configuration stored in .ows/config.json.
/// </summary>
public sealed class OwsProjectConfig {
    /// <summary>Gets the OWS version.</summary>
    [JsonPropertyName("owsVersion")]
    public string OwsVersion { get; init; } = "0.1";

    /// <summary>Gets the project root path.</summary>
    [JsonPropertyName("projectRoot")]
    public string? ProjectRoot { get; init; }

    /// <summary>Gets when the project was initialized.</summary>
    [JsonPropertyName("initializedAtUtc")]
    public DateTimeOffset InitializedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets local file watcher settings.</summary>
    [JsonPropertyName("watcherSettings")]
    public FileWatcherOptions? WatcherSettings { get; init; }
}
