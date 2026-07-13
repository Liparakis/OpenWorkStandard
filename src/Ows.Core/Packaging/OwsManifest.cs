namespace Ows.Core.Packaging;

/// <summary>
///     Describes the metadata stored in an OWS submission package manifest.
/// </summary>
public sealed record OwsManifest {
    /// <summary>
    ///     Gets the package format version.
    /// </summary>
    public string OwsVersion { get; init; } = "0.1";

    /// <summary>
    ///     Gets the timestamp when the package manifest was generated.
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///     Gets the unique package identifier.
    /// </summary>
    public string PackageId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    ///     Gets the human-readable project name.
    /// </summary>
    public string ProjectName { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the platform where the package was generated.
    /// </summary>
    public string Platform { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the toolchain used to create the package.
    /// </summary>
    public string Toolchain { get; init; } = ".NET";

    /// <summary>
    ///     Gets the relative path to the tracked project root.
    /// </summary>
    public string TrackedPath { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the SHA-256 hash of the packaged timeline content.
    /// </summary>
    public string TimelineHash { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the SHA-256 hash of the packaged version graph content.
    /// </summary>
    public string VersionGraphHash { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the SHA-256 hash of the canonical logical package root.
    /// </summary>
    public string PackageRootHash { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the signing algorithm identifier when the package is signed.
    /// </summary>
    public string SignatureAlgorithm { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the signer public-key fingerprint when the package is signed.
    /// </summary>
    public string SignatureKeyFingerprint { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the SHA-256 hashes of packaged artifact entries keyed by archive path.
    /// </summary>
    public IReadOnlyDictionary<string, string> ArtifactHashes { get; init; } = new Dictionary<string, string>();
}
