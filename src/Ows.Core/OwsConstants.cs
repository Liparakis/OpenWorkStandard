namespace Ows.Core;

/// <summary>
/// Defines shared constants for local OWS storage and package contents.
/// </summary>
public static class OwsConstants
{
    /// <summary>
    /// Gets the folder name used for local OWS evidence inside a tracked project.
    /// </summary>
    public const string LocalFolderName = ".ows";

    /// <summary>
    /// Gets the file extension used for OWS submission packages.
    /// </summary>
    public const string PackageExtension = ".owspkg";

    /// <summary>
    /// Gets the manifest file name stored inside a package.
    /// </summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>
    /// Gets the append-only timeline file name stored inside a package.
    /// </summary>
    public const string TimelineFileName = "timeline.jsonl";

    /// <summary>
    /// Gets the version graph file name stored inside a package.
    /// </summary>
    public const string VersionGraphFileName = "version_graph.json";
}
