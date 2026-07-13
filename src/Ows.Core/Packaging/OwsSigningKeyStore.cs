using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace Ows.Core.Packaging;

/// <summary>
///     Stores and loads the user's local RSA package-signing key.
/// </summary>
public sealed class OwsSigningKeyStore {
    /// <summary>
    ///     The file path where the signing key is stored.
    /// </summary>
    private readonly string _keyPath;

    /// <summary>Initializes a key store at the configured or supplied path.</summary>
    /// <param name="keyPath">The optional path used to store the signing key.</param>
    public OwsSigningKeyStore(string? keyPath = null) {
        _keyPath = keyPath ?? GetDefaultKeyPath();
    }

    /// <summary>Gets the default user-local signing key path.</summary>
    /// <returns>The path used for the user-local signing key.</returns>
    public static string GetDefaultKeyPath() {
        var configured = Environment.GetEnvironmentVariable("OWS_PACKAGE_SIGNING_KEY_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) {
            return Path.GetFullPath(configured);
        }

        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(root, "OpenWorkStandard", "signing-key.json");
    }

    /// <summary>Loads the existing key or creates a new user-local RSA key.</summary>
    /// <returns>A signer backed by the existing or newly created key.</returns>
    public OwsPackageSigner GetOrCreateSigner() {
        if (File.Exists(_keyPath)) {
            return LoadSigner();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_keyPath) ?? ".");
        using var rsa = RSA.Create(3072);
        var privateKey = rsa.ExportPkcs8PrivateKey();
        var protectedKey = ProtectForCurrentUser(privateKey);
        var keyFile = new SigningKeyFile {
            PrivateKeyBase64 = Convert.ToBase64String(protectedKey),
            Protection = OperatingSystem.IsWindows() ? "WindowsDpapiCurrentUser" : "UnixUserFileMode",
            PublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem()
        };
        var tempPath = $"{_keyPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(keyFile));
        RestrictFile(tempPath);
        try {
            File.Move(tempPath, _keyPath);
        } catch (IOException) when (File.Exists(_keyPath)) {
            File.Delete(tempPath);
        }

        return LoadSigner();
    }

    /// <summary>
    ///     Loads the signing key from the key file and initializes an OwsPackageSigner.
    /// </summary>
    /// <returns>A configured <see cref="OwsPackageSigner" /> instance.</returns>
    private OwsPackageSigner LoadSigner() {
        var keyFile = JsonSerializer.Deserialize<SigningKeyFile>(File.ReadAllText(_keyPath))
                      ?? throw new InvalidDataException("OWS signing key file is empty.");
        var rsa = RSA.Create();
        try {
            if (!string.IsNullOrWhiteSpace(keyFile.PrivateKeyBase64)) {
                var privateKey = Convert.FromBase64String(keyFile.PrivateKeyBase64);
                if (string.Equals(keyFile.Protection, "WindowsDpapiCurrentUser", StringComparison.Ordinal)) {
                    privateKey = UnprotectForCurrentUser(privateKey);
                }

                rsa.ImportPkcs8PrivateKey(privateKey, out _);
            } else {
                // ponytail: accept the pre-DPAPI local format; newly created keys use protected bytes.
                rsa.ImportFromPem(keyFile.PrivateKeyPem);
            }

            var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
            if (!string.Equals(publicKeyPem, keyFile.PublicKeyPem, StringComparison.Ordinal)) {
                throw new InvalidDataException("OWS signing key public/private material does not match.");
            }

            return new OwsPackageSigner(rsa, publicKeyPem);
        } catch {
            rsa.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Restricts filesystem permissions of the specified file to user read/write access on Unix-like systems.
    /// </summary>
    /// <param name="path">The file path to restrict.</param>
    private static void RestrictFile(string path) {
        if (!OperatingSystem.IsWindows()) {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    ///     Encrypts the private key bytes using DPAPI if on Windows, or returns them unchanged on other platforms.
    /// </summary>
    /// <returns>The protected or original private key byte array.</returns>
    /// <param name="privateKey">The raw private key bytes to protect.</param>
    private static byte[] ProtectForCurrentUser(byte[] privateKey) {
        return OperatingSystem.IsWindows() ? ProtectWindows(privateKey) : privateKey;
    }

    /// <summary>
    ///     Decrypts the private key bytes using DPAPI if on Windows, or returns them unchanged on other platforms.
    /// </summary>
    /// <returns>The unprotected or original private key byte array.</returns>
    /// <param name="privateKey">The protected private key bytes to decrypt.</param>
    private static byte[] UnprotectForCurrentUser(byte[] privateKey) {
        return OperatingSystem.IsWindows() ? UnprotectWindows(privateKey) : privateKey;
    }

    /// <summary>
    ///     Encrypts the private key bytes using the Windows Data Protection API (DPAPI).
    /// </summary>
    /// <returns>The encrypted private key bytes.</returns>
    /// <param name="privateKey">The raw private key bytes.</param>
    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(byte[] privateKey) {
        return ProtectedData.Protect(privateKey, null, DataProtectionScope.CurrentUser);
    }

    /// <summary>
    ///     Decrypts the private key bytes using the Windows Data Protection API (DPAPI).
    /// </summary>
    /// <returns>The decrypted private key bytes.</returns>
    /// <param name="privateKey">The encrypted private key bytes.</param>
    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWindows(byte[] privateKey) {
        return ProtectedData.Unprotect(privateKey, null, DataProtectionScope.CurrentUser);
    }

    private sealed record SigningKeyFile {
        /// <summary>Gets the base64-encoded private key, potentially protected by DPAPI.</summary>
        public string PrivateKeyBase64 { get; init; } = string.Empty;

        /// <summary>Gets the protection mechanism name applied to the private key.</summary>
        public string Protection { get; init; } = string.Empty;

        /// <summary>Gets the private key PEM string, used in legacy unencrypted format.</summary>
        public string PrivateKeyPem { get; init; } = string.Empty;

        /// <summary>Gets the public key PEM string.</summary>
        public string PublicKeyPem { get; init; } = string.Empty;
    }
}

/// <summary>
///     Performs RSA-SHA256 package-root signatures using one local key.
/// </summary>
public sealed class OwsPackageSigner : IDisposable {
    /// <summary>
    ///     The RSA cryptographic instance used for signing.
    /// </summary>
    private readonly RSA _rsa;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OwsPackageSigner" /> class.
    /// </summary>
    /// <param name="rsa">The RSA instance to use for signing.</param>
    /// <param name="publicKeyPem">The public key material in PEM format.</param>
    internal OwsPackageSigner(RSA rsa, string publicKeyPem) {
        _rsa = rsa;
        PublicKeyPem = publicKeyPem;
        KeyFingerprint = Convert.ToHexString(SHA256.HashData(_rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
    }

    /// <summary>Gets the public key in PEM format.</summary>
    public string PublicKeyPem { get; }

    /// <summary>Gets the lowercase SHA-256 public-key fingerprint.</summary>
    public string KeyFingerprint { get; }

    /// <summary>Releases the private-key handle.</summary>
    public void Dispose() {
        _rsa.Dispose();
    }

    /// <summary>Signs canonical package-root bytes.</summary>
    /// <param name="data">The canonical bytes to sign.</param>
    /// <returns>The RSA signature for the supplied bytes.</returns>
    public byte[] Sign(ReadOnlySpan<byte> data) {
        return _rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}
