using System.Text;
using Ows.Core.Hashing;

namespace Ows.Core.Agent;

/// <summary>
/// Computes deterministic hashes for observed snapshot state.
/// </summary>
public static class SnapshotHashCalculator
{
    /// <summary>
    /// Computes a canonical hash for the supplied observed snapshot.
    /// </summary>
    /// <param name="snapshot">The snapshot to hash.</param>
    /// <returns>The lower-case SHA-256 digest of the canonical snapshot representation.</returns>
    public static string ComputeHash(ObservedSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var builder = new StringBuilder();
        builder.Append("observedAt=").Append(snapshot.ObservedAt.ToUniversalTime().ToString("O")).Append('\n');

        foreach (var file in snapshot.Files.Values
                     .OrderBy(state => NormalizePath(state.RelativePath), StringComparer.Ordinal))
        {
            builder
                .Append(NormalizePath(file.RelativePath)).Append('|')
                .Append(file.FileHash).Append('|')
                .Append(file.Size).Append('|')
                .Append(file.LineCount).Append('|')
                .Append(file.LastWriteTime.ToUniversalTime().ToString("O")).Append('|')
                .Append(file.ObservedAt.ToUniversalTime().ToString("O")).Append('\n');
        }

        return new Sha256HashService().ComputeHash(builder.ToString());
    }

    /// <summary>
    /// Normalizes backslashes to forward slashes for cross-platform canonical path representation.
    /// </summary>
    /// <param name="relativePath">The path to normalize.</param>
    /// <returns>The normalized forward-slash path string.</returns>
    private static string NormalizePath(string relativePath) =>
        (relativePath).Replace('\\', '/');
}