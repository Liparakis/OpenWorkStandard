namespace Ows.Core.Graph;

/// <summary>
/// Represents a directed edge between two version nodes.
/// </summary>
public sealed record WorkVersionEdge
{
    /// <summary>
    /// Gets the parent version identifier.
    /// </summary>
    public string ParentVersionId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the child version identifier.
    /// </summary>
    public string ChildVersionId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the event identifier that produced the transition, when known.
    /// </summary>
    public Guid? EventId { get; init; }

    /// <summary>
    /// Gets a human-readable description of the transformation.
    /// </summary>
    public string Description { get; init; } = string.Empty;
}