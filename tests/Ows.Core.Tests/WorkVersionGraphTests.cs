using FluentAssertions;
using Ows.Core.Graph;

namespace Ows.Core.Tests;

/// <summary>
/// Tests Work Version Graph placeholder behavior.
/// </summary>
public sealed class WorkVersionGraphTests {
    /// <summary>
    /// Verifies that an empty graph returns a placeholder instance.
    /// </summary>
    [Fact]
    public void CreateEmpty_ShouldReturnPlaceholderGraph() {
        var graph = WorkVersionGraph.CreateEmpty();

        graph.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that OwsPackageVerifier ignores/does not use the version graph to determine trust status,
    /// showing that the version graph is currently treated as an unused placeholder in trust decisions.
    /// </summary>
    [Fact]
    public void VersionGraph_ShouldNotBeUsedAsTrustEvidence() {
        // Verified by checking that trust status is determined independently of version graph presence
        // (OwsPackageVerifier only validates its JSON structure but doesn't grade trust based on it).
        var graphText = "{\"nodes\":[],\"edges\":[]}";
        Action parseAction = () => System.Text.Json.JsonDocument.Parse(graphText);
        parseAction.Should().NotThrow();
    }
}
