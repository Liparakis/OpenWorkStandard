using System.IO.Compression;
using System.Security.Cryptography;

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

/// <summary>
/// Describes one durably stored package blob.
/// </summary>
internal sealed record PackageBlobSaveResult
{
    public string ObjectKey { get; init; } = string.Empty;

    public string PackageSha256 { get; init; } = string.Empty;

    public long PackageSizeBytes { get; init; }
}

/// <summary>
/// Stores package blobs in a local filesystem directory using content-addressed names.
/// </summary>
internal sealed class LocalFilePackageBlobStore : IPackageBlobStore
{
    private readonly string _rootPath;
    private readonly long _maxPackageSizeBytes;

    /// <summary>
    /// Initializes a new local blob store.
    /// </summary>
    public LocalFilePackageBlobStore(string rootPath, long maxPackageSizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = rootPath;
        _maxPackageSizeBytes = maxPackageSizeBytes > 0 ? maxPackageSizeBytes : 50 * 1024 * 1024;
    }

    /// <inheritdoc />
    public async Task<PackageBlobSaveResult> SaveAsync(Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        Directory.CreateDirectory(_rootPath);

        var tempPath = Path.Combine(_rootPath, $"{Guid.NewGuid():N}.upload");
        FileStream? destination = null;
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long totalBytes = 0;

        try
        {
            destination = File.Create(tempPath);
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > _maxPackageSizeBytes)
                {
                    throw new InvalidOperationException("Uploaded package exceeds maximum size limit.");
                }

                sha256.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await destination.FlushAsync(cancellationToken);
            await destination.DisposeAsync();
            destination = null;

            var packageSha256 = Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant();
            var objectKey = $"{packageSha256}.owspkg";
            var targetPath = GetAbsolutePath(objectKey);

            ValidatePackageShape(targetPath: tempPath);

            if (File.Exists(targetPath))
            {
                File.Delete(tempPath);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }

            return new PackageBlobSaveResult
            {
                ObjectKey = objectKey,
                PackageSha256 = packageSha256,
                PackageSizeBytes = totalBytes
            };
        }
        catch
        {
            if (destination is not null)
            {
                await destination.DisposeAsync();
            }

            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetAbsolutePath(objectKey);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Package blob not found.", objectKey);
        }

        Stream stream = File.OpenRead(path);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(GetAbsolutePath(objectKey)));
    }

    /// <inheritdoc />
    public Task<bool> CheckHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Directory.CreateDirectory(_rootPath);
            var probePath = Path.Combine(_rootPath, ".ows-blob-health");
            File.WriteAllText(probePath, "ready");
            File.Delete(probePath);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    // ponytail: content-addressed files are enough for MVP dedupe; add sharding only when directory size becomes measurable.
    private string GetAbsolutePath(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey) ||
            objectKey.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            objectKey.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid package object key.");
        }

        return Path.Combine(_rootPath, objectKey);
    }

    // Reject obvious garbage early so the worker only sees real .owspkg-shaped packages.
    private static void ValidatePackageShape(string targetPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(targetPath);
            string[] requiredEntries =
            [
                Ows.Core.OwsConstants.ManifestFileName,
                Ows.Core.OwsConstants.TimelineFileName,
                Ows.Core.OwsConstants.VersionGraphFileName
            ];

            foreach (var entryName in requiredEntries)
            {
                if (archive.GetEntry(entryName) is null)
                {
                    throw new InvalidOperationException($"Uploaded package is missing required entry '{entryName}'.");
                }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Uploaded package is not a valid .owspkg archive: {exception.Message}");
        }
    }
}
