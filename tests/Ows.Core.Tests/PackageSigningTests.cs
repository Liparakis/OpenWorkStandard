using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ows.Core;
using Ows.Core.Events;
using Ows.Core.Packaging;
using Ows.Core.Verification;

namespace Ows.Core.Tests;

/// <summary>
/// Verifies canonical package roots, optional signatures, and tamper handling.
/// </summary>
public sealed class PackageSigningTests {
    [Fact]
    public async Task UnsignedPackage_ShouldRemainLocallyValidWithExplicitUnsignedState() {
        var fixture = CreateFixture();
        try {
            await CreatePackageAsync(fixture, sign: false);
            var result = await VerifyAsync(fixture.PackagePath);

            result.IsSuccess.Should().BeTrue();
            result.SignatureStatus.Should().Be("Unsigned");
            result.Package.PackageRootHash.Should().NotBeNullOrWhiteSpace();
        } finally {
            DeleteFixture(fixture);
        }
    }

    [Fact]
    public async Task SignedPackage_ShouldVerifyOfflineAndIgnoreArchiveOrdering() {
        var fixture = CreateFixture();
        var reorderedPath = Path.Combine(fixture.Root, "reordered.owspkg");
        try {
            await CreatePackageAsync(fixture, sign: true);
            RewritePackage(fixture.PackagePath, reorderedPath, (entry, content) => (entry, content));
            var result = await VerifyAsync(reorderedPath);

            result.IsSuccess.Should().BeTrue();
            result.SignatureStatus.Should().Be("Valid");
            result.Package.PackageRootHash.Should().NotBeNullOrWhiteSpace();
        } finally {
            DeleteFixture(fixture);
        }
    }

    [Fact]
    public async Task RepeatedBuilds_ShouldKeepTheLogicalPackageRootStable() {
        var fixture = CreateFixture();
        try {
            await CreatePackageAsync(fixture, sign: false);
            var firstRoot = ReadPackageRoot(fixture.PackagePath);
            File.Delete(fixture.PackagePath);

            await CreatePackageAsync(fixture, sign: false);

            ReadPackageRoot(fixture.PackagePath).Should().Be(firstRoot);
        } finally {
            DeleteFixture(fixture);
        }
    }

    [Theory]
    [InlineData("artifact")]
    [InlineData("timeline")]
    [InlineData("manifest")]
    [InlineData("signature")]
    public async Task SignedPackage_ShouldRejectTamperedEntry(string tamperKind) {
        var fixture = CreateFixture();
        var tamperedPath = Path.Combine(fixture.Root, $"{tamperKind}.owspkg");
        try {
            await CreatePackageAsync(fixture, sign: true);
            RewritePackage(fixture.PackagePath, tamperedPath, (entry, content) => tamperKind switch {
                "artifact" when entry == "artifacts/src/main.cs" => (entry, Encoding.UTF8.GetBytes("changed")),
                "timeline" when entry == OwsConstants.TimelineFileName =>
                    (entry, Encoding.UTF8.GetBytes("{}\n")),
                "manifest" when entry == OwsConstants.ManifestFileName => (entry, TamperManifest(content)),
                "signature" when entry == OwsConstants.SignatureFileName =>
                    (entry, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new OwsPackageSignature {
                        RootHash = "tampered",
                        KeyFingerprint = "tampered",
                        PublicKeyPem = "tampered",
                        SignatureBase64 = "tampered"
                    }))),
                _ => (entry, content)
            });

            var result = await VerifyAsync(tamperedPath);

            result.IsSuccess.Should().BeFalse();
            if (tamperKind is "manifest" or "signature") {
                result.SignatureStatus.Should().Be("Invalid");
            }
            result.TrustStatus.Should().Be(TrustStatus.Invalid);
        } finally {
            DeleteFixture(fixture);
        }
    }

    [Fact]
    public async Task SignedPackage_ShouldRejectRemovedAndInjectedEntries() {
        var fixture = CreateFixture();
        var removedPath = Path.Combine(fixture.Root, "removed.owspkg");
        var injectedPath = Path.Combine(fixture.Root, "injected.owspkg");
        try {
            await CreatePackageAsync(fixture, sign: true);
            RewritePackage(fixture.PackagePath, removedPath,
                (entry, content) => entry == "artifacts/src/main.cs" ? (null, content) : (entry, content));
            RewritePackage(fixture.PackagePath, injectedPath, (entry, content) => (entry, content),
                ("artifacts/injected.txt", Encoding.UTF8.GetBytes("injected")));

            (await VerifyAsync(removedPath)).IsSuccess.Should().BeFalse();
            (await VerifyAsync(injectedPath)).IsSuccess.Should().BeFalse();
        } finally {
            DeleteFixture(fixture);
        }
    }

    [Fact]
    public async Task Verify_ShouldReturnInvalidForMalformedZipInput() {
        var packagePath = Path.Combine(Path.GetTempPath(), $"ows-malformed-{Guid.NewGuid():N}.owspkg");
        try {
            File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

            var result = await VerifyAsync(packagePath);

            result.IsSuccess.Should().BeFalse();
            result.TrustStatus.Should().Be(TrustStatus.Invalid);
            result.Errors.Should().Contain(error => error.Contains("ZIP", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (File.Exists(packagePath)) {
                File.Delete(packagePath);
            }
        }
    }

    [Fact]
    public void SigningKeyStore_ShouldProtectNewWindowsKeyMaterial() {
        var keyPath = Path.Combine(Path.GetTempPath(), $"ows-key-{Guid.NewGuid():N}.json");
        try {
            using var signer = new OwsSigningKeyStore(keyPath).GetOrCreateSigner();
            var keyFile = File.ReadAllText(keyPath);

            keyFile.Contains("BEGIN RSA PRIVATE KEY", StringComparison.Ordinal).Should().BeFalse();
            if (OperatingSystem.IsWindows()) {
                keyFile.Contains("WindowsDpapiCurrentUser", StringComparison.Ordinal).Should().BeTrue();
            }
        } finally {
            if (File.Exists(keyPath)) {
                File.Delete(keyPath);
            }
        }
    }

    private static async Task CreatePackageAsync(Fixture fixture, bool sign) {
        await new OwsPackageBuilder().CreatePackageAsync(new PackageCreationRequest {
            ProjectRootPath = fixture.Root,
            OutputPackagePath = fixture.PackagePath,
            SignPackage = sign,
            SigningKeyPath = fixture.KeyPath
        }, CancellationToken.None);
    }

    private static async Task<VerificationResult> VerifyAsync(string packagePath) =>
        await new OwsPackageVerifier().VerifyAsync(new PackageVerificationRequest {
            PackagePath = packagePath
        }, CancellationToken.None);

    private static string ReadPackageRoot(string packagePath) {
        using var archive = ZipFile.OpenRead(packagePath);
        using var reader = new StreamReader(archive.GetEntry(OwsConstants.ManifestFileName)!.Open());
        return JsonSerializer.Deserialize<OwsManifest>(reader.ReadToEnd())!.PackageRootHash;
    }

    private static void RewritePackage(
        string sourcePath,
        string destinationPath,
        Func<string, byte[], (string? Name, byte[] Content)> transform,
        (string Name, byte[] Content)? extra = null) {
        using var source = ZipFile.OpenRead(sourcePath);
        using var destination = ZipFile.Open(destinationPath, ZipArchiveMode.Create);
        foreach (var sourceEntry in source.Entries.Reverse()) {
            using var stream = sourceEntry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var transformed = transform(sourceEntry.FullName, memory.ToArray());
            if (transformed.Name is null) {
                continue;
            }

            var entry = destination.CreateEntry(transformed.Name);
            using var output = entry.Open();
            output.Write(transformed.Content);
        }

        if (extra is { } addition) {
            var entry = destination.CreateEntry(addition.Name);
            using var output = entry.Open();
            output.Write(addition.Content);
        }
    }

    private static Fixture CreateFixture() {
        var root = Path.Combine(Path.GetTempPath(), $"ows-signing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, ".ows"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "main.cs"), "class Main {}\n");
        var timelineEvent = OwsEventChain.CreateChainedEvent(new OwsEvent {
            EventType = OwsEventType.FileCreated,
            ProjectId = "signing-fixture",
            RelativePath = "src/main.cs"
        }, OwsEventChain.GenesisPreviousEventHash);
        File.WriteAllText(Path.Combine(root, ".ows", OwsConstants.TimelineFileName),
            JsonSerializer.Serialize(timelineEvent) + Environment.NewLine);
        return new Fixture(root, Path.Combine(root, "submission.owspkg"), Path.Combine(root, "signing-key.json"));
    }

    private static byte[] TamperManifest(byte[] content) {
        var manifest = JsonSerializer.Deserialize<OwsManifest>(content)! with {
            ProjectName = "tampered"
        };
        return JsonSerializer.SerializeToUtf8Bytes(manifest);
    }

    private static void DeleteFixture(Fixture fixture) {
        if (Directory.Exists(fixture.Root)) {
            Directory.Delete(fixture.Root, recursive: true);
        }
    }

    private sealed record Fixture(string Root, string PackagePath, string KeyPath);
}
