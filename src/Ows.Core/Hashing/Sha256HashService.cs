using System.Security.Cryptography;
using System.Text;

namespace Ows.Core.Hashing;

/// <summary>
/// Computes SHA-256 hashes for OWS content and metadata.
/// </summary>
public sealed class Sha256HashService : IHashService
{
    /// <inheritdoc />
    public string ComputeHash(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <inheritdoc />
    public string ComputeHash(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ComputeHash(Encoding.UTF8.GetBytes(text));
    }
}
