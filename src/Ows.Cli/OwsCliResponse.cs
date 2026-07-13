namespace Ows.Cli;

/// <summary>
///     Unified response structure returned by the local OWS CLI.
/// </summary>
public sealed class OwsCliResponse {
    /// <summary>Gets or sets a value indicating whether the command succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Gets or sets the local command status.</summary>
    public string? Status { get; set; }

    /// <summary>Gets or sets the current project root.</summary>
    public string? ProjectRoot { get; set; }

    /// <summary>Gets or sets a value indicating whether the Agent watcher is running.</summary>
    public bool WatcherRunning { get; set; }

    /// <summary>Gets or sets the human-readable result message.</summary>
    public string? Message { get; set; }

    /// <summary>Gets command errors.</summary>
    public List<string> Errors { get; } = [];

    /// <summary>Returns the response shape used by JSON output.</summary>
    /// <returns>An object containing the response fields used by JSON output.</returns>
    public object ToSerializableModel() {
        return new {
            Success,
            Status,
            ProjectRoot,
            WatcherRunning,
            Message,
            Errors
        };
    }
}
