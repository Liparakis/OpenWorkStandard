namespace Ows.Core.Hashing;

/// <summary>
/// Defines hashing operations used by OWS integrity checks.
/// </summary>
public interface IHashService {
    /// <summary>
    /// Computes a SHA-256 hash for the provided binary data.
    /// </summary>
    /// <param name="data">The input data.</param>
    /// <returns>The lower-case hexadecimal digest.</returns>
    string ComputeHash(byte[] data);

    /// <summary>
    /// Computes a SHA-256 hash for the provided text using UTF-8 encoding.
    /// </summary>
    /// <param name="text">The input text.</param>
    /// <returns>The lower-case hexadecimal digest.</returns>
    string ComputeHash(string text);
}
