using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Ows.Core.Events;
using Ows.Core.Packaging;
using Ows.Core.Verification;

namespace Ows.Core.Tests;

/// <summary>
///     Verifies canonical package roots, optional signatures, and tamper handling.
/// </summary>
public sealed class PackageSigningTests {
    /// <summary>
    ///     Verifies that an unsigned package is successfully verified offline with a signature status of "Unsigned".
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task UnsignedPackage_ShouldRemainLocallyValidWithExplicitUnsignedState() {
        var fixture = CreateFixture();
        try {
            await CreatePackageAsync(fixture, false);
            var result = await VerifyAsync(fixture.PackagePath);

            result.IsSuccess.Should().BeTrue();
            result.SignatureStatus.Should().Be("Unsigned");
            result.Package.PackageRootHash.Should().NotBeNullOrWhiteSpace();
        } finally {
            DeleteFixture(fixture);
        }
    }

    /// <summary>
    ///     Verifies that a signed package verifies successfully offline and that verification ignores the order of entries
    ///     inside the zip archive.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SignedPackage_ShouldVerifyOfflineAndIgnoreArchiveOrdering() {
        var fixture = CreateFixture();
        var reorderedPath = Path.Combine(fixture.Root, "reordered.owspkg");
        try {
            await CreatePackageAsync(fixture, true);
            RewritePackage(fixture.PackagePath, reorderedPath, (entry, content) => (entry, content));
            var result = await VerifyAsync(reorderedPath);

            result.IsSuccess.Should().BeTrue();
            result.SignatureStatus.Should().Be("Valid");
            result.Package.PackageRootHash.Should().NotBeNullOrWhiteSpace();
        } finally {
            DeleteFixture(fixture);
        }
    }

    /// <summary>
    ///     Verifies that creating a package multiple times from the same source state produces a stable package root hash.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task RepeatedBuilds_ShouldKeepTheLogicalPackageRootStable() {
        var fixture = CreateFixture();
        try {
            await CreatePackageAsync(fixture, false);
            var firstRoot = ReadPackageRoot(fixture.PackagePath);
            File.Delete(fixture.PackagePath);

            await CreatePackageAsync(fixture, false);

            ReadPackageRoot(fixture.PackagePath).Should().Be(firstRoot);
        } finally {
            DeleteFixture(fixture);
        }
    }

    /// <summary>
    ///     Verifies that package verification fails when package entries (artifacts, timeline, manifest, or signature) are
    ///     tampered with.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous test operation.</returns>
    /// <param name="tamperKind">The component type being tampered with.</param>
    [Theory]
    [InlineData("artifact")]
    [InlineData("timeline")]
    [InlineData("manifest")]
    [InlineData("signature")]
    public async Task SignedPackage_ShouldRejectTamperedEntry(string tamperKind) {
        var fixture = CreateFixture();
        var tamperedPath = Path.Combine(fixture.Root, $"{tamperKind}.owspkg");
        try {
            await CreatePackageAsync(fixture, true);
            RewritePackage(
                fixture.PackagePath, tamperedPath, (entry, content) => tamperKind switch {
                    "artifact" when entry == "artifacts/src/main.cs" => (entry, Encoding.UTF8.GetBytes("changed")),
                    "timeline" when entry == OwsConstants.TimelineFileName =>
                        (entry, Encoding.UTF8.GetBytes("{}\n")),
                    "manifest" when entry == OwsConstants.ManifestFileName => (entry, TamperManifest(content)),
                    "signature" when entry == OwsConstants.SignatureFileName =>
                        (entry, Encoding.UTF8.GetBytes(
                            JsonSerializer.Serialize(
                                new OwsPackageSignature {
                                    RootHash = "tampered",
                                    KeyFingerprint = "tampered",
                                    PublicKeyPem = "tampered",
                                    SignatureBase64 = "tampered"
                                }
                            )
                        )),
                    _ => (entry, content)
                }
            );

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

    /// <summary>
    ///     Verifies that package verification fails when file entries are deleted or when untracked files are injected into
    ///     the zip archive.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task SignedPackage_ShouldRejectRemovedAndInjectedEntries() {
        var fixture = CreateFixture();
        var removedPath = Path.Combine(fixture.Root, "removed.owspkg");
        var injectedPath = Path.Combine(fixture.Root, "injected.owspkg");
        try {
            await CreatePackageAsync(fixture, true);
            RewritePackage(
                fixture.PackagePath, removedPath,
                (entry, content) => entry == "artifacts/src/main.cs" ? (null, content) : (entry, content)
            );
            RewritePackage(
                fixture.PackagePath, injectedPath, (entry, content) => (entry, content),
                ("artifacts/injected.txt", Encoding.UTF8.GetBytes("injected"))
            );

            (await VerifyAsync(removedPath)).IsSuccess.Should().BeFalse();
            (await VerifyAsync(injectedPath)).IsSuccess.Should().BeFalse();
        } finally {
            DeleteFixture(fixture);
        }
    }

    /// <summary>
    ///     Verifies that package verification fails and reports a readable ZIP error when the package file contains malformed
    ///     data.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous test operation.</returns>
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

    /// <summary>
    ///     Verifies that new signing keys created on Windows are protected using DPAPI.
    /// </summary>
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

    /// <summary>
    ///     Helper method to generate an OWS package for a test fixture.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous package creation operation.</returns>
    /// <param name="fixture">The test fixture container.</param>
    /// <param name="sign">Whether the package should be signed.</param>
    private static async Task CreatePackageAsync(Fixture fixture, bool sign) {
        await OwsPackageBuilder.CreatePackageAsync(
            new PackageCreationRequest {
                ProjectRootPath = fixture.Root,
                OutputPackagePath = fixture.PackagePath,
                SignPackage = sign,
                SigningKeyPath = fixture.KeyPath
            }, CancellationToken.None
        );
    }

    /// <summary>
    ///     Helper method to run OWS package verification on the specified package file path.
    /// </summary>
    /// <returns>A task returning the verification result.</returns>
    /// <param name="packagePath">The file path to the package to verify.</param>
    private static async Task<VerificationResult> VerifyAsync(string packagePath) {
        return await OwsPackageVerifier.VerifyAsync(
            new PackageVerificationRequest {
                PackagePath = packagePath
            }, CancellationToken.None
        );
    }

    /// <summary>
    ///     Helper method to read the package root hash directly from the manifest in the package archive.
    /// </summary>
    /// <returns>The package root hash string.</returns>
    /// <param name="packagePath">The file path to the package archive.</param>
    private static string ReadPackageRoot(string packagePath) {
        using var archive = ZipFile.OpenRead(packagePath);
        using var reader = new StreamReader(archive.GetEntry(OwsConstants.ManifestFileName)!.Open());
        return JsonSerializer.Deserialize<OwsManifest>(reader.ReadToEnd())!.PackageRootHash;
    }

    /// <summary>
    ///     Re-writes a package zip archive, applying transforms to existing entries and optionally adding extra ones.
    /// </summary>
    /// <param name="sourcePath">The file path of the source package.</param>
    /// <param name="destinationPath">The file path of the destination package.</param>
    /// <param name="transform">A function transforming entry name and content.</param>
    /// <param name="extra">An optional extra entry to insert.</param>
    private static void RewritePackage(
        string sourcePath,
        string destinationPath,
        Func<string, byte[], (string? Name, byte[] Content)> transform,
        (string Name, byte[] Content)? extra = null
    ) {
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

    /// <summary>
    ///     Creates a new test fixture directory and populates it with a sample project structure, a timeline event, and paths.
    /// </summary>
    /// <returns>The initialized <see cref="Fixture" /> instance.</returns>
    private static Fixture CreateFixture() {
        var root = Path.Combine(Path.GetTempPath(), $"ows-signing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, ".ows"));
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "main.cs"), "class Main {}\n");
        var timelineEvent = OwsEventChain.CreateChainedEvent(
            new OwsEvent {
                EventType = OwsEventType.FileCreated,
                ProjectId = "signing-fixture",
                RelativePath = "src/main.cs"
            }, OwsEventChain.GenesisPreviousEventHash
        );
        File.WriteAllText(
            Path.Combine(root, ".ows", OwsConstants.TimelineFileName),
            JsonSerializer.Serialize(timelineEvent) + Environment.NewLine
        );
        return new Fixture(root, Path.Combine(root, "submission.owspkg"), Path.Combine(root, "signing-key.json"));
    }

    /// <summary>
    ///     Deserializes manifest content, changes the project name value to "tampered", and serializes it back to UTF-8 bytes.
    /// </summary>
    /// <returns>The tampered manifest JSON bytes.</returns>
    /// <param name="content">The original manifest bytes to modify.</param>
    private static byte[] TamperManifest(byte[] content) {
        var manifest = JsonSerializer.Deserialize<OwsManifest>(content)! with {
            ProjectName = "tampered"
        };
        return JsonSerializer.SerializeToUtf8Bytes(manifest);
    }

    /// <summary>
    ///     Cleans up the test fixture by deleting its directory.
    /// </summary>
    /// <param name="fixture">The test fixture container.</param>
    private static void DeleteFixture(Fixture fixture) {
        if (Directory.Exists(fixture.Root)) {
            Directory.Delete(fixture.Root, true);
        }
    }

    private sealed record Fixture(string Root, string PackagePath, string KeyPath);
}
