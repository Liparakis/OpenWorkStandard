using FluentAssertions;
using Ows.Packaging;

namespace Ows.Packaging.Tests;

/// <summary>
/// Tests the package builder skeleton.
/// </summary>
public sealed class OwsPackageBuilderTests
{
    /// <summary>
    /// Verifies the current placeholder response.
    /// </summary>
    [Fact]
    public async Task CreatePackageAsync_ShouldReportNotImplemented()
    {
        var builder = new OwsPackageBuilder();

        var result = await builder.CreatePackageAsync(
            new PackageCreationRequest
            {
                ProjectRootPath = "samples/sample-project",
                OutputPackagePath = "submission.owspkg"
            },
            CancellationToken.None);

        result.Created.Should().BeFalse();
        result.Message.Should().Be("OWS package: not implemented yet");
    }
}
