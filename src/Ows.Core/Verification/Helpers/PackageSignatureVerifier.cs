using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Ows.Core.Hashing;
using Ows.Core.Packaging;

namespace Ows.Core.Verification.Helpers;

/// <summary>
///     Represents the <see cref="PackageSignatureVerifier" /> type.
/// </summary>
internal static class PackageSignatureVerifier {
    /// <summary>
    ///     Validates the package signature against the manifest and archive contents.
    /// </summary>
    /// <returns>A string indicating the signature status ("Valid", "Unsigned", or "Invalid").</returns>
    /// <param name="archive">The zip archive containing the package files.</param>
    /// <param name="manifest">The package manifest.</param>
    /// <param name="errors">A list to populate with signature validation error messages.</param>
    public static string Validate(
        ZipArchive archive,
        OwsManifest manifest,
        List<string> errors
    ) {
        var signatureEntry = archive.GetEntry(OwsConstants.SignatureFileName);
        if (string.IsNullOrWhiteSpace(manifest.PackageRootHash)) {
            if (signatureEntry is not null) {
                errors.Add("Package signature exists without a package root hash.");
                return "Invalid";
            }

            return "Unsigned";
        }

        var rootBytes = PackageRootCanonicalizer.BuildCanonicalBytes(manifest);
        var expectedRoot = Sha256HashService.ComputeHash(rootBytes);
        if (!string.Equals(expectedRoot, manifest.PackageRootHash, StringComparison.OrdinalIgnoreCase)) {
            errors.Add("Package root hash does not match canonical package contents.");
        }

        if (signatureEntry is null) {
            return "Unsigned";
        }

        OwsPackageSignature? signature;
        try {
            using var reader = new StreamReader(signatureEntry.Open());
            signature = JsonSerializer.Deserialize<OwsPackageSignature>(reader.ReadToEnd());
        } catch (Exception ex) {
            errors.Add($"Invalid package signature metadata: {ex.Message}");
            return "Invalid";
        }

        if (signature is null ||
            !string.Equals(signature.RootHash, manifest.PackageRootHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                signature.KeyFingerprint, manifest.SignatureKeyFingerprint, StringComparison.OrdinalIgnoreCase
            ) ||
            !string.Equals(signature.Algorithm, manifest.SignatureAlgorithm, StringComparison.Ordinal)) {
            errors.Add("Package signature metadata does not match the manifest.");
            return "Invalid";
        }

        try {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(signature.PublicKeyPem);
            var fingerprint = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
            var valid = string.Equals(fingerprint, signature.KeyFingerprint, StringComparison.OrdinalIgnoreCase) &&
                        rsa.VerifyData(
                            rootBytes, Convert.FromBase64String(signature.SignatureBase64),
                            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1
                        );
            if (!valid) {
                errors.Add("Package signature verification failed.");
                return "Invalid";
            }

            return "Valid";
        } catch (Exception ex) {
            errors.Add($"Package signature verification failed: {ex.Message}");
            return "Invalid";
        }
    }
}
