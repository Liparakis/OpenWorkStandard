namespace Ows.Core.Graph;

/// <summary>
/// Placeholder for future Work Version Graph support in package metadata.
/// </summary>
public sealed record WorkVersionGraph {
    // TODO: Add real version-graph nodes, edges, and validation when OWS starts emitting graph data.

    /// <summary>
    /// Creates an empty Work Version Graph.
    /// </summary>
    /// <returns>An empty placeholder graph.</returns>
    public static WorkVersionGraph CreateEmpty() => new();
}
