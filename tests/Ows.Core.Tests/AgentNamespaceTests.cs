using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Ows.Core.Agent;
using Ows.Core.Events;

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

    /// <summary>
    /// Verifies that starting the agent appends file events for existing project files.
    /// </summary>
    [Fact]
    public async Task StartAsync_ShouldAppendExistingFilesToTimeline()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var trackedFile = Path.Combine(projectRoot, "notes.txt");
        File.WriteAllText(trackedFile, "hello");

        try
        {
            var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            Directory.CreateDirectory(localFolder);
            var timelinePath = Path.Combine(localFolder, OwsConstants.TimelineFileName);
            File.WriteAllText(timelinePath, string.Empty);

            var agent = new LocalTrackingAgent(new NullLogger<LocalTrackingAgent>());
            await agent.PrepareAsync(
                new TrackingAgentOptions
                {
                    ProjectRootPath = projectRoot,
                    DatabasePath = Path.Combine(localFolder, "ows.db")
                },
                CancellationToken.None);

            var result = await agent.StartAsync(CancellationToken.None);
            var lines = File.ReadAllLines(timelinePath);

            result.Succeeded.Should().BeTrue();
            result.Status.Should().Be(TrackingAgentStatus.Ready);
            lines.Should().ContainSingle();
            lines[0].Should().Contain(nameof(OwsEventType.FileCreated));
            lines[0].Should().Contain("notes.txt");
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }
}
