namespace Ows.Core.Agent;

/// <summary>
/// Identifies the kind of file-system change detected by the watcher.
/// </summary>
public enum FileChangeKind
{
    /// <summary>A file was created inside the tracked project boundary.</summary>
    Created,

    /// <summary>A file was modified inside the tracked project boundary.</summary>
    Modified,

    /// <summary>A file was deleted from inside the tracked project boundary.</summary>
    Deleted,

    /// <summary>A file was renamed inside the tracked project boundary.</summary>
    Renamed
}

/// <summary>
/// Represents a debounced file-system notification produced by <see cref="OwsFileWatcher"/>
/// before it is appended to the provenance timeline.
/// </summary>
/// <param name="RelativePath">The project-relative path of the affected file.</param>
/// <param name="ChangeKind">The kind of change detected.</param>
public sealed record FileWatchEvent(string RelativePath, FileChangeKind ChangeKind);