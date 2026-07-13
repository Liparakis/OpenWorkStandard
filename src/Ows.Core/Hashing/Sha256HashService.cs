using System.Security.Cryptography;
using System.Text;

namespace Ows.Core.Hashing;

/// <summary>
///     Computes SHA-256 hashes for OWS content and metadata.
/// </summary>
public sealed class Sha256HashService {
    /// <summary>
    ///     Computes a SHA-256 hash for the supplied byte array.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public static string ComputeHash(byte[] data) {
        ArgumentNullException.ThrowIfNull(data);

        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    ///     Computes a SHA-256 hash for the supplied UTF-8 string.
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public string ComputeHash(string text) {
        ArgumentNullException.ThrowIfNull(text);
        return ComputeHash(Encoding.UTF8.GetBytes(text));
    }
}
