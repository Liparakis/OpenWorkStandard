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
}
