using System.IO.Compression;
using System.Security.Cryptography;

namespace Ows.Verifier.Server;

/// <summary>
/// Describes one durably stored package blob.
/// </summary>
internal sealed record PackageBlobSaveResult {
    /// <summary>
    /// Gets the storage object key under which the package is saved.
    /// </summary>
    public string ObjectKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets the computed SHA-256 hash of the package.
    /// </summary>
    public string PackageSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Gets the total size of the package in bytes.
    /// </summary>
    public long PackageSizeBytes { get; init; }
}

/// <summary>
/// Stores package blobs in a local filesystem directory using content-addressed names.
/// </summary>
internal sealed class LocalFilePackageBlobStore : IPackageBlobStore {
    private const int MaxPackageEntryPathLength = 240;
    private const long MaxCompressionRatio = 200;

    /// <summary>
    /// The root directory path on the local filesystem where blobs are stored.
    /// </summary>
    private readonly string _rootPath;

    /// <summary>
    /// The maximum allowed package size in bytes.
    /// </summary>
    private readonly long _maxPackageSizeBytes;
    private readonly long _maxPackageExpandedBytes;
    private readonly int _maxPackageEntryCount;

    /// <summary>
    /// Initializes a new local blob store.
    /// </summary>
    /// <param name="rootPath">The root folder path where uploaded packages will be written.</param>
    /// <param name="maxPackageSizeBytes">The maximum allowed size of an uploaded package in bytes.</param>
    /// <param name="maxPackageExpandedBytes">The maximum allowed total uncompressed bytes across archive entries.</param>
    /// <param name="maxPackageEntryCount">The maximum allowed archive entry count.</param>
    public LocalFilePackageBlobStore(
        string rootPath,
        long maxPackageSizeBytes,
        long maxPackageExpandedBytes,
        int maxPackageEntryCount) {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = rootPath;
        _maxPackageSizeBytes = maxPackageSizeBytes > 0 ? maxPackageSizeBytes : 50 * 1024 * 1024;
        _maxPackageExpandedBytes = maxPackageExpandedBytes > 0 ? maxPackageExpandedBytes : 200 * 1024 * 1024;
        _maxPackageEntryCount = maxPackageEntryCount > 0 ? maxPackageEntryCount : 512;
    }

    /// <inheritdoc />
    public async Task<PackageBlobSaveResult> SaveAsync(Stream source, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(source);
        Directory.CreateDirectory(_rootPath);

        var tempPath = Path.Combine(_rootPath, $"{Guid.NewGuid():N}.upload");
        FileStream? destination = null;
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long totalBytes = 0;

        try {
            destination = File.Create(tempPath);
            while (true) {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) {
                    break;
                }

                totalBytes += read;
                if (totalBytes > _maxPackageSizeBytes) {
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

            ValidatePackageShape(tempPath, _maxPackageExpandedBytes, _maxPackageEntryCount);

            if (File.Exists(targetPath)) {
                File.Delete(tempPath);
            } else {
                File.Move(tempPath, targetPath);
            }

            return new PackageBlobSaveResult {
                ObjectKey = objectKey,
                PackageSha256 = packageSha256,
                PackageSizeBytes = totalBytes
            };
        } catch {
            if (destination is not null) {
                await destination.DisposeAsync();
            }

            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetAbsolutePath(objectKey);
        if (!File.Exists(path)) {
            throw new FileNotFoundException("Package blob not found.", objectKey);
        }

        Stream stream = File.OpenRead(path);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(GetAbsolutePath(objectKey)));
    }

    /// <inheritdoc />
    public Task<bool> CheckHealthAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        try {
            Directory.CreateDirectory(_rootPath);
            var probePath = Path.Combine(_rootPath, ".ows-blob-health");
            File.WriteAllText(probePath, "ready");
            File.Delete(probePath);
            return Task.FromResult(true);
        } catch {
            return Task.FromResult(false);
        }
    }

    // ponytail: content-addressed files are enough for MVP dedupe; add sharding only when directory size becomes measurable.
    /// <summary>
    /// Combines the object key with the root path and validates that the key does not escape the boundary.
    /// </summary>
    /// <param name="objectKey">The content-addressed key of the package blob.</param>
    /// <returns>The verified absolute path to the package blob.</returns>
    private string GetAbsolutePath(string objectKey) {
        if (string.IsNullOrWhiteSpace(objectKey) ||
            objectKey.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            objectKey.Contains("..", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Invalid package object key.");
        }

        return Path.Combine(_rootPath, objectKey);
    }

    // Reject obvious garbage early so the worker only sees real .owspkg-shaped packages.
    /// <summary>
    /// Validates that the package file contains all required OWS metadata entries.
    /// </summary>
    /// <param name="targetPath">The local file path to the archive file.</param>
    /// <param name="maxExpandedBytes">The maximum allowed total uncompressed size across all archive entries.</param>
    /// <param name="maxEntryCount">The maximum allowed number of archive entries.</param>
    private static void ValidatePackageShape(string targetPath, long maxExpandedBytes, int maxEntryCount) {
        try {
            using var archive = ZipFile.OpenRead(targetPath);
            if (archive.Entries.Count > maxEntryCount) {
                throw new InvalidOperationException($"Uploaded package contains too many entries ({archive.Entries.Count}).");
            }

            string[] requiredEntries =
            [
                Core.OwsConstants.ManifestFileName,
                    Core.OwsConstants.TimelineFileName,
                    Core.OwsConstants.VersionGraphFileName
            ];

            var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalExpandedBytes = 0;
            foreach (var entry in archive.Entries) {
                var normalizedName = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(normalizedName)) {
                    throw new InvalidOperationException("Uploaded package contains an empty archive entry name.");
                }

                if (normalizedName.Length > MaxPackageEntryPathLength) {
                    throw new InvalidOperationException(
                        $"Uploaded package entry path '{normalizedName}' exceeds the maximum allowed length.");
                }

                if (normalizedName.StartsWith("/", StringComparison.Ordinal) ||
                    normalizedName.Contains("../", StringComparison.Ordinal) ||
                    normalizedName.Contains(":/", StringComparison.Ordinal)) {
                    throw new InvalidOperationException(
                        $"Uploaded package entry path '{normalizedName}' is not allowed.");
                }

                if (!seenEntries.Add(normalizedName)) {
                    throw new InvalidOperationException(
                        $"Uploaded package contains duplicate archive entry '{normalizedName}'.");
                }

                totalExpandedBytes += Math.Max(0, entry.Length);
                if (totalExpandedBytes > maxExpandedBytes) {
                    throw new InvalidOperationException("Uploaded package expands beyond the maximum safe limit.");
                }

                if (entry.CompressedLength > 0 &&
                    entry.Length / (double) entry.CompressedLength > MaxCompressionRatio) {
                    throw new InvalidOperationException(
                        $"Uploaded package entry '{normalizedName}' has an unsafe compression ratio.");
                }
            }

            foreach (var entryName in requiredEntries) {
                if (archive.GetEntry(entryName) is null) {
                    throw new InvalidOperationException($"Uploaded package is missing required entry '{entryName}'.");
                }
            }
        } catch (InvalidOperationException) {
            throw;
        } catch (Exception exception) {
            throw new InvalidOperationException(
                $"Uploaded package is not a valid .owspkg archive: {exception.Message}");
        }
    }
}
