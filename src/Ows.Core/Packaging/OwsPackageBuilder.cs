using System.Text.Json;
using Ows.Core.Hashing;
using Ows.Core.Ignore;
using Ows.Core.Init;

namespace Ows.Core.Packaging;

/// <summary>
/// Provides the OWS package builder that delegates to focused helper services.
/// </summary>
public sealed class OwsPackageBuilder {
    /// <inheritdoc />
    public Task<PackageCreationResult> CreatePackageAsync(PackageCreationRequest request,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var localFolder = Path.Combine(request.ProjectRootPath, OwsConstants.LocalFolderName);
        var timelinePath = Path.Combine(localFolder, OwsConstants.TimelineFileName);
        var outputDirectory = Path.GetDirectoryName(request.OutputPackagePath);
        var hashService = new Sha256HashService();

        if (!string.IsNullOrWhiteSpace(outputDirectory)) {
            Directory.CreateDirectory(outputDirectory);
        }

        var timelineText = File.ReadAllText(timelinePath);
        const string versionGraphText = "{\"nodes\":[],\"edges\":[]}";
        var artifactHashes = PackageArtifactCollector.CollectArtifacts(
            request.ProjectRootPath, request.OutputPackagePath, hashService,
            LoadIgnoreEngine(request.ProjectRootPath));

        var manifest = PackageManifestBuilder.BuildManifest(
            request.ProjectRootPath, timelineText, versionGraphText, artifactHashes, hashService);
        manifest = manifest with { PackageRootHash = PackageRootCanonicalizer.ComputeHash(manifest) };

        OwsPackageSignature? signature = null;
        if (request.SignPackage) {
            using var signer = new OwsSigningKeyStore(request.SigningKeyPath).GetOrCreateSigner();
            manifest = manifest with {
                SignatureAlgorithm = "RSA-SHA256-PKCS1-v1_5",
                SignatureKeyFingerprint = signer.KeyFingerprint
            };
            var rootBytes = PackageRootCanonicalizer.BuildCanonicalBytes(manifest);
            signature = new OwsPackageSignature {
                RootHash = manifest.PackageRootHash,
                KeyFingerprint = signer.KeyFingerprint,
                PublicKeyPem = signer.PublicKeyPem,
                SignatureBase64 = Convert.ToBase64String(signer.Sign(rootBytes))
            };
        }

        PackageArchiveWriter.WriteArchive(
            request.OutputPackagePath,
            request.ProjectRootPath,
            manifest,
            timelineText,
            versionGraphText,
            artifactHashes,
            signature);

        return Task.FromResult(new PackageCreationResult {
            Created = true
        });
    }

    private static OwsIgnoreEngine LoadIgnoreEngine(string projectRootPath) {
        var configPath = Path.Combine(projectRootPath, OwsConstants.LocalFolderName, "config.json");
        var additionalDirectoryNames = Array.Empty<string>();

        if (File.Exists(configPath)) {
            var config = JsonSerializer.Deserialize<OwsProjectConfig>(File.ReadAllText(configPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            additionalDirectoryNames = config?.WatcherSettings?.ExcludeDirectories ?? [];
        }

        return OwsIgnoreEngine.Load(projectRootPath, additionalDirectoryNames);
    }
}
