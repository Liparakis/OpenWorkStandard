namespace Ows.Core.Packaging;

/// <summary>
///     Describes the inputs required to create an OWS package.
/// </summary>
public sealed record PackageCreationRequest {
    /// <summary>
    ///     Gets the tracked project root path.
    /// </summary>
    public string ProjectRootPath { get; init; } = string.Empty;

    /// <summary>
    ///     Gets the output package path.
    /// </summary>
    public string OutputPackagePath { get; init; } = string.Empty;

    /// <summary>
    ///     Gets a value indicating whether the package should be signed with the local signing key.
    /// </summary>
    public bool SignPackage { get; init; }

    /// <summary>
    ///     Gets the optional local signing-key path.
    /// </summary>
    public string? SigningKeyPath { get; init; }
}
