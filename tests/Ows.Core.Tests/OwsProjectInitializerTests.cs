using System.Text.Json;
using FluentAssertions;
using Ows.Core.Init;

namespace Ows.Core.Tests;

/// <summary>
/// Tests project initialization for local OWS state.
/// </summary>
public sealed class OwsProjectInitializerTests {
    /// <summary>
    /// Verifies that initialization creates the local OWS folder and starter files.
    /// </summary>
    [Fact]
    public void Initialize_ShouldCreateLocalFolderConfigAndTimeline() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-init-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);

        try {
            var initializer = new OwsProjectInitializer();

            var result = initializer.Initialize(projectRoot);

            var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            var configPath = Path.Combine(localFolder, "config.json");
            var timelinePath = Path.Combine(localFolder, OwsConstants.TimelineFileName);

            result.LocalFolderPath.Should().Be(localFolder);
            Directory.Exists(localFolder).Should().BeTrue();
            File.Exists(configPath).Should().BeTrue();
            File.Exists(timelinePath).Should().BeTrue();

            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            document.RootElement.GetProperty("owsVersion").GetString().Should().Be("0.1");
            document.RootElement.GetProperty("projectRoot").GetString().Should().Be(projectRoot);
        } finally {
            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }
}
