namespace Ows.Core.Packaging;

/// <summary>
/// Represents the outcome of a package creation request.
/// </summary>
public sealed record PackageCreationResult
{
    /// <summary>
    /// Gets a value indicating whether a package was created.
    /// </summary>
    public bool Created { get; init; }

    /// <summary>
    /// Gets the intended output package path.
    /// </summary>
    public string OutputPackagePath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the message describing the current outcome.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}