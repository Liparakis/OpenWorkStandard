using FluentAssertions;
using Ows.Core.Packaging;

namespace Ows.Core.Tests;

/// <summary>
/// Tests packaging types after consolidation into Ows.Core.
/// </summary>
public sealed class PackagingNamespaceTests
{
    /// <summary>
    /// Verifies the package builder skeleton still reports an unimplemented state.
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
