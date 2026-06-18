using FluentAssertions;
using Ows.Verification;

namespace Ows.Verification.Tests;

/// <summary>
/// Tests the package verifier skeleton.
/// </summary>
public sealed class OwsPackageVerifierTests
{
    /// <summary>
    /// Verifies the current placeholder response.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_ShouldReturnFailurePlaceholder()
    {
        var verifier = new OwsPackageVerifier();

        var result = await verifier.VerifyAsync(
            new PackageVerificationRequest { PackagePath = "submission.owspkg" },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Summary.Should().Be("OWS verify: not implemented yet");
    }
}
