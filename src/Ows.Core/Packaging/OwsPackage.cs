using Ows.Core.Events;
using Ows.Core.Graph;

namespace Ows.Core.Packaging;

/// <summary>
/// Represents a package payload assembled for submission or verification workflows.
/// </summary>
public sealed record OwsPackage
{
    /// <summary>
    /// Gets the package file path.
    /// </summary>
    public string PackagePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the package manifest.
    /// </summary>
    public required OwsManifest Manifest { get; init; }

    /// <summary>
    /// Gets the timeline events carried by the package.
    /// </summary>
    public IReadOnlyList<OwsEvent> TimelineEntries { get; init; } = Array.Empty<OwsEvent>();

    /// <summary>
    /// Gets the work version graph carried by the package.
    /// </summary>
    public required WorkVersionGraph VersionGraph { get; init; }
}