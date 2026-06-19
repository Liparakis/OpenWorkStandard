namespace Ows.Core.Graph;

/// <summary>
/// Represents the directed acyclic graph that models work evolution over time.
/// </summary>
public sealed record WorkVersionGraph
{
    /// <summary>
    /// Gets the root version identifier, when the graph has an initial node.
    /// </summary>
    public string? RootVersionId { get; init; }

    /// <summary>
    /// Gets the version nodes in the graph.
    /// </summary>
    public IReadOnlyList<WorkVersionNode> Nodes { get; init; } = Array.Empty<WorkVersionNode>();

    /// <summary>
    /// Gets the directed edges in the graph.
    /// </summary>
    public IReadOnlyList<WorkVersionEdge> Edges { get; init; } = Array.Empty<WorkVersionEdge>();

    /// <summary>
    /// Creates an empty Work Version Graph.
    /// </summary>
    /// <returns>A graph with no nodes or edges.</returns>
    public static WorkVersionGraph CreateEmpty() => new();
}