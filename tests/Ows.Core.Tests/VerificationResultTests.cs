using FluentAssertions;
using Ows.Core.Verification;

namespace Ows.Core.Tests;

/// <summary>
/// Tests verification result helpers.
/// </summary>
public sealed class VerificationResultTests
{
    /// <summary>
    /// Verifies successful result construction.
    /// </summary>
    [Fact]
    public void Success_ShouldCreateSuccessfulResult()
    {
        var result = VerificationResult.Success("Verified");

        result.IsSuccess.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Summary.Should().Be("Verified");
    }

    /// <summary>
    /// Verifies failed result construction.
    /// </summary>
    [Fact]
    public void Failure_ShouldCreateFailedResultWithErrors()
    {
        var result = VerificationResult.Failure("Failed", ["hash mismatch"]);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Be("hash mismatch");
        result.Summary.Should().Be("Failed");
    }
}
