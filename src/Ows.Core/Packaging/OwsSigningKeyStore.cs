using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Ows.Core.Packaging;

/// <summary>
/// Stores and loads the user's local RSA package-signing key.
/// </summary>
public sealed class OwsSigningKeyStore {
    private readonly string _keyPath;

    /// <summary>Initializes a key store at the configured or supplied path.</summary>
    public OwsSigningKeyStore(string? keyPath = null) {
        _keyPath = keyPath ?? GetDefaultKeyPath();
    }

    /// <summary>Gets the default user-local signing key path.</summary>
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

    private static void RestrictFile(string path) {
        if (!OperatingSystem.IsWindows()) {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static byte[] ProtectForCurrentUser(byte[] privateKey) =>
        OperatingSystem.IsWindows() ? ProtectWindows(privateKey) : privateKey;

    private static byte[] UnprotectForCurrentUser(byte[] privateKey) =>
        OperatingSystem.IsWindows() ? UnprotectWindows(privateKey) : privateKey;

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(byte[] privateKey) =>
        ProtectedData.Protect(privateKey, null, DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWindows(byte[] privateKey) =>
        ProtectedData.Unprotect(privateKey, null, DataProtectionScope.CurrentUser);

    private sealed record SigningKeyFile {
        public string PrivateKeyBase64 { get; init; } = string.Empty;
        public string Protection { get; init; } = string.Empty;
        public string PrivateKeyPem { get; init; } = string.Empty;
        public string PublicKeyPem { get; init; } = string.Empty;
    }
}

/// <summary>
/// Performs RSA-SHA256 package-root signatures using one local key.
/// </summary>
public sealed class OwsPackageSigner : IDisposable {
    private readonly RSA _rsa;

    internal OwsPackageSigner(RSA rsa, string publicKeyPem) {
        _rsa = rsa;
        PublicKeyPem = publicKeyPem;
        KeyFingerprint = Convert.ToHexString(SHA256.HashData(_rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
    }

    /// <summary>Gets the public key in PEM format.</summary>
    public string PublicKeyPem { get; }
    /// <summary>Gets the lowercase SHA-256 public-key fingerprint.</summary>
    public string KeyFingerprint { get; }

    /// <summary>Signs canonical package-root bytes.</summary>
    public byte[] Sign(ReadOnlySpan<byte> data) =>
        _rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    /// <summary>Releases the private-key handle.</summary>
    public void Dispose() => _rsa.Dispose();
}
