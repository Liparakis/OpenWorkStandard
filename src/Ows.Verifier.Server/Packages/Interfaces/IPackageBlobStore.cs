namespace Ows.Verifier.Server;

/// <summary>
/// Stores uploaded package blobs outside the verifier database.
/// </summary>
internal interface IPackageBlobStore
{
    /// <summary>
    /// Saves a package blob durably and returns the server-side object metadata.
    /// </summary>
    Task<PackageBlobSaveResult> SaveAsync(Stream source, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a saved package blob for reading.
    /// </summary>
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken);

    /// <summary>
    /// Returns whether a saved package blob exists.
    /// </summary>
    Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether the storage root is usable.
    /// </summary>
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken);
}
