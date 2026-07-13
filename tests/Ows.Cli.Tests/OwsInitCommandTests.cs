using FluentAssertions;
using Ows.Core.Agent;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests the init command behavior.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class OwsInitCommandTests {
    /// <summary>
    /// Verifies that the init command creates local OWS state in the current directory.
    /// </summary>
    [Fact]
    public async Task InitCommand_ShouldCreateLocalOwsStateInCurrentDirectory() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-init-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var originalDirectory = Directory.GetCurrentDirectory();
        var registryPath = Path.Combine(projectRoot, "agent-registry.json");
        var originalRegistryPath = Environment.GetEnvironmentVariable("OWS_AGENT_REGISTRY_PATH");

        try {
            Directory.SetCurrentDirectory(projectRoot);
            Environment.SetEnvironmentVariable("OWS_AGENT_REGISTRY_PATH", registryPath);

            var parseResult = OwsCommandFactory.BuildRootCommand().Parse(["init"]);
            var exitCode = await parseResult.InvokeAsync();

            exitCode.Should().Be(0);
            Directory.Exists(Path.Combine(projectRoot, ".ows")).Should().BeTrue();
            File.Exists(Path.Combine(projectRoot, ".ows", "config.json")).Should().BeTrue();
            File.Exists(Path.Combine(projectRoot, ".ows", "timeline.jsonl")).Should().BeTrue();
            new OwsProjectRegistry(registryPath).GetProjects()
                .Should().ContainSingle(project => string.Equals(project.ProjectRootPath, projectRoot,
                    StringComparison.OrdinalIgnoreCase));
        } finally {
            Directory.SetCurrentDirectory(originalDirectory);
            Environment.SetEnvironmentVariable("OWS_AGENT_REGISTRY_PATH", originalRegistryPath);

            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }
}
