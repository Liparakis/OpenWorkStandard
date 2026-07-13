namespace Ows.Core.Packaging;

/// <summary>
/// Describes an optional offline-verifiable package signature.
/// </summary>
public sealed record OwsPackageSignature {
    /// <summary>Gets the signature algorithm identifier.</summary>
    public string Algorithm { get; init; } = "RSA-SHA256-PKCS1-v1_5";
    /// <summary>Gets the signed package-root hash.</summary>
    public string RootHash { get; init; } = string.Empty;
    /// <summary>Gets the SHA-256 fingerprint of the public key.</summary>
    public string KeyFingerprint { get; init; } = string.Empty;
    /// <summary>Gets the public key in PEM format.</summary>
    public string PublicKeyPem { get; init; } = string.Empty;
    /// <summary>Gets the signature bytes encoded as Base64.</summary>
    public string SignatureBase64 { get; init; } = string.Empty;
}
