using System.Text.Json;
using FluentAssertions;
using Ows.Core;
using Ows.Core.Agent;

namespace Ows.Cli.Tests;

/// <summary>
///     Represents the <see cref="CliHardeningTests" /> type.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class CliHardeningTests {
    /// <summary>
    ///     Verifies that the OWS status command returns an error status when the watcher has crashed.
    /// </summary>
    /// <returns>A <see cref="Task" /> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task Cli_ShouldReturnErrorStatus_WhenWatcherCrashed() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-crash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var originalDirectory = Directory.GetCurrentDirectory();

        var originalOut = Console.Out;
        var originalError = Console.Error;

        try {
            Directory.SetCurrentDirectory(projectRoot);

            // Initialize OWS
            var manager = new OwsProjectAgent();
            manager.InitializeProject(projectRoot);

            // Write stale lock file to simulate watcher crash
            var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            var watcherJsonPath = Path.Combine(localFolder, "watcher.json");
            var staleState = new WatcherProcessState {
                Pid = 999999, // Unlikely to exist
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            };
            File.WriteAllText(watcherJsonPath, JsonSerializer.Serialize(staleState));

            using (var sw = new StringWriter()) {
                Console.SetOut(sw);
                var parseResult = OwsCommandFactory.BuildRootCommand().Parse(["status", "--json"]);
                var exitCode = await parseResult.InvokeAsync();

                exitCode.Should().Be(1);

                var output = ExtractJson(sw.ToString());
                var doc = JsonDocument.Parse(output);
                doc.RootElement.GetProperty("Success").GetBoolean().Should().BeFalse();
                doc.RootElement.GetProperty("Status").GetString().Should().Be("Error");
                doc.RootElement.GetProperty("Errors")[0].GetString().Should()
                   .Contain("Watcher has crashed or is not running.");
            }
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot)) {
                try {
                    Directory.Delete(projectRoot, true);
                } catch {
                }
            }
        }
    }

    /// <summary>
    ///     Extracts the JSON object substring from the provided text.
    /// </summary>
    /// <returns>The extracted JSON string, or the original text if no JSON brackets are found.</returns>
    /// <param name="text">The raw text content containing JSON.</param>
    private static string ExtractJson(string text) {
        var startIdx = text.IndexOf('{');
        var endIdx = text.LastIndexOf('}');
        if (startIdx >= 0 && endIdx > startIdx) {
            return text.Substring(startIdx, endIdx - startIdx + 1);
        }

        return text;
    }
}
