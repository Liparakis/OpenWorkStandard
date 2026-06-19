namespace Ows.Core.Graph;

/// <summary>
/// Represents a node in the Work Version Graph.
/// </summary>
public sealed record WorkVersionNode
{
    /// <summary>
    /// Gets the unique version identifier.
    /// </summary>
    public string VersionId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the SHA-256 hash that identifies the version content.
    /// </summary>
    public string ContentHash { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC timestamp when the version node was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the parent version identifiers that feed this node.
    /// </summary>
    public IReadOnlyList<string> ParentHashes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets optional metadata for the version node.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}