using FluentAssertions;
using Ows.Core.Verification;

namespace Ows.Core.Tests;

/// <summary>
/// Tests verification types after consolidation into Ows.Core.
/// </summary>
public sealed class VerificationNamespaceTests
{
    /// <summary>
    /// Verifies the package verifier skeleton still reports an unimplemented state.
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
