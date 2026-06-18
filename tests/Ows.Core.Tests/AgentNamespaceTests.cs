using FluentAssertions;
using Ows.Core.Agent;

namespace Ows.Core.Tests;

/// <summary>
/// Tests agent types after consolidation into Ows.Core.
/// </summary>
public sealed class AgentNamespaceTests
{
    /// <summary>
    /// Verifies the tracking agent status enum is exposed from Ows.Core.
    /// </summary>
    [Fact]
    public void TrackingAgentStatus_ShouldExposeIdleState()
    {
        TrackingAgentStatus.Idle.Should().Be(TrackingAgentStatus.Idle);
    }
}
