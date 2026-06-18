using FluentAssertions;
using Ows.Core;

namespace Ows.Core.Tests;

/// <summary>
/// Tests OWS constants.
/// </summary>
public sealed class OwsConstantsTests
{
    /// <summary>
    /// Verifies the local evidence folder name.
    /// </summary>
    [Fact]
    public void LocalFolderName_ShouldMatchExpectedValue()
    {
        OwsConstants.LocalFolderName.Should().Be(".ows");
    }

    /// <summary>
    /// Verifies the package extension.
    /// </summary>
    [Fact]
    public void PackageExtension_ShouldMatchExpectedValue()
    {
        OwsConstants.PackageExtension.Should().Be(".owspkg");
    }
}
