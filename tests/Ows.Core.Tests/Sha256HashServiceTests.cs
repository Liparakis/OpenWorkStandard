using FluentAssertions;
using Ows.Core.Hashing;

namespace Ows.Core.Tests;

/// <summary>
///     Tests SHA-256 hashing behavior.
/// </summary>
public sealed class Sha256HashServiceTests {
    /// <summary>
    ///     Verifies that equal text produces equal hashes.
    /// </summary>
    [Fact]
    public void ComputeHash_ForSameText_ReturnsSameDigest() {
        var service = new Sha256HashService();

        var firstHash = service.ComputeHash("ows");
        var secondHash = service.ComputeHash("ows");

        firstHash.Should().Be(secondHash);
    }

    /// <summary>
    ///     Verifies that text and byte hashing align for the same content.
    /// </summary>
    [Fact]
    public void ComputeHash_ForEquivalentTextAndBytes_ReturnsSameDigest() {
        var service = new Sha256HashService();

        var textHash = service.ComputeHash("Open Work Standard");
        var byteHash = Sha256HashService.ComputeHash("Open Work Standard"u8.ToArray());

        textHash.Should().Be(byteHash);
    }
}
