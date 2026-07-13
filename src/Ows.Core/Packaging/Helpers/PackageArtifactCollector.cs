using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ows.Core.Hashing;
using Ows.Core.Ignore;

namespace Ows.Core.Packaging;

internal static class PackageArtifactCollector {
    public static Dictionary<string, string> CollectArtifacts(string projectRootPath, string outputPackagePath,
        Sha256HashService hashService, OwsIgnoreEngine ignoreEngine) {
        ArgumentNullException.ThrowIfNull(ignoreEngine);

        return Directory
            .EnumerateFiles(projectRootPath, "*", SearchOption.AllDirectories)
            .Select(filePath => new {
                FilePath = filePath,
                RelativePath = Path.GetRelativePath(projectRootPath, filePath)
            })
            .Where(file =>
                !string.Equals(file.FilePath, outputPackagePath, StringComparison.OrdinalIgnoreCase) &&
                !file.RelativePath.StartsWith($"{OwsConstants.LocalFolderName}{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase) &&
                !ignoreEngine.IsIgnored(file.RelativePath))
            .OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                file => $"artifacts/{file.RelativePath.Replace('\\', '/')}",
                file => hashService.ComputeHash(File.ReadAllBytes(file.FilePath)),
                StringComparer.Ordinal);
    }
}
