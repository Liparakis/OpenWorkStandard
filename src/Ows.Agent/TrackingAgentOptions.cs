namespace Ows.Agent;

/// <summary>
/// Configures the local tracking agent skeleton.
/// </summary>
public sealed record TrackingAgentOptions
{
    /// <summary>
    /// Gets the root path of the tracked project.
    /// </summary>
    public string ProjectRootPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the path to the local SQLite database file.
    /// </summary>
    public string DatabasePath { get; init; } = string.Empty;
}
