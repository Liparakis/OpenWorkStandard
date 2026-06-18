namespace Ows.Packaging;

/// <summary>
/// Provides the initial OWS package builder skeleton.
/// </summary>
public sealed class OwsPackageBuilder : IPackageBuilder
{
    /// <inheritdoc />
    public Task<PackageCreationResult> CreatePackageAsync(PackageCreationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new PackageCreationResult
        {
            Created = false,
            OutputPackagePath = request.OutputPackagePath,
            Message = "OWS package: not implemented yet"
        });
    }
}
