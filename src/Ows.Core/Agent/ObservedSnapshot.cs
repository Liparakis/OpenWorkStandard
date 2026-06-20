using System;
using System.Collections.Generic;

namespace Ows.Core.Agent;

/// <summary>
/// Represents the observed state of a single project file at a specific time.
/// </summary>
public sealed class ObservedFileState
{
    /// <summary>
    /// Gets or sets the relative file path.
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SHA-256 hash of the file contents.
    /// </summary>
    public string FileHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the estimated line count of the file.
    /// </summary>
    public int LineCount { get; set; }

    /// <summary>
    /// Gets or sets the last write time of the file.
    /// </summary>
    public DateTimeOffset LastWriteTime { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when this file state was observed.
    /// </summary>
    public DateTimeOffset ObservedAt { get; set; }
}

/// <summary>
/// Represents a snapshot of all observed project files.
/// </summary>
public sealed class ObservedSnapshot
{
    /// <summary>
    /// Gets or sets the timestamp when the snapshot was taken.
    /// </summary>
    public DateTimeOffset ObservedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the dictionary of file states indexed by relative path.
    /// </summary>
    public Dictionary<string, ObservedFileState> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
