using Ows.Core.Hashing;
using Ows.Core.Ignore;

namespace Ows.Core.Packaging.Helpers;

/// <summary>
/// Represents the <see cref="PackageArtifactCollector"/> type.
/// </summary>
internal static class PackageArtifactCollector {
    /// <summary>
    /// Collects and hashes all non-ignored project files relative to the project root.
    /// </summary>
    /// <returns>A dictionary mapping package-relative artifact paths to their SHA-256 hashes.</returns>
    /// <param name="projectRootPath">The root directory path of the project.</param>
    /// <param name="outputPackagePath">The file path of the output package (excluded from collection).</param>
    /// <param name="hashService">The hash service used to compute file digests.</param>
    /// <param name="ignoreEngine">The ignore engine used to filter out ignored files.</param>
    public static Dictionary<string, string> CollectArtifacts(
        string projectRootPath,
        string outputPackagePath,
        Sha256HashService hashService,
        OwsIgnoreEngine ignoreEngine
    ) {
        ArgumentNullException.ThrowIfNull(ignoreEngine);

        return Directory
               .EnumerateFiles(projectRootPath, "*", SearchOption.AllDirectories)
               .Select(filePath => new {
                       FilePath = filePath,
                       RelativePath = Path.GetRelativePath(projectRootPath, filePath)
                   }
               )
               .Where(file =>
                   !string.Equals(file.FilePath, outputPackagePath, StringComparison.OrdinalIgnoreCase) &&
                   !file.RelativePath.StartsWith(
                       $"{OwsConstants.LocalFolderName}{Path.DirectorySeparatorChar}",
                       StringComparison.OrdinalIgnoreCase
                   ) &&
                   !ignoreEngine.IsIgnored(file.RelativePath)
               )
               .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
               .ToDictionary(
                   file => $"artifacts/{file.RelativePath.Replace('\\', '/')}",
                   file => Sha256HashService.ComputeHash(File.ReadAllBytes(file.FilePath)),
                   StringComparer.Ordinal
               );
    }
}
