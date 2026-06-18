using FluentAssertions;
using Ows.Core.Graph;

namespace Ows.Core.Tests;

/// <summary>
/// Tests Work Version Graph defaults.
/// </summary>
public sealed class WorkVersionGraphTests
{
    /// <summary>
    /// Verifies that an empty graph starts with no nodes and no edges.
    /// </summary>
    [Fact]
    public void CreateEmpty_ShouldReturnGraphWithoutNodesOrEdges()
    {
        var graph = WorkVersionGraph.CreateEmpty();

        graph.Nodes.Should().BeEmpty();
        graph.Edges.Should().BeEmpty();
        graph.RootVersionId.Should().BeNull();
    }
}
