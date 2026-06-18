namespace Ows.Core.Packaging;

/// <summary>
/// Defines the package creation contract for OWS submission archives.
/// </summary>
public interface IPackageBuilder
{
    /// <summary>
    /// Attempts to create an OWS package.
    /// </summary>
    /// <param name="request">The package creation request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result describing the placeholder status.</returns>
    Task<PackageCreationResult> CreatePackageAsync(PackageCreationRequest request, CancellationToken cancellationToken);
}
