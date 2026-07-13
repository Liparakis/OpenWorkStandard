using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Ows.Core;
using Ows.Core.Agent;
using Xunit;

namespace Ows.Cli.Tests;

[Collection(CliCommandCollection.Name)]
public sealed class CliHardeningTests {
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
            var manager = new OwsWatchSessionManager();
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
                doc.RootElement.GetProperty("Errors")[0].GetString().Should().Contain("Watcher has crashed or is not running.");
            }
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot)) {
                try { Directory.Delete(projectRoot, recursive: true); } catch { }
            }
        }
    }

    private static string ExtractJson(string text) {
        var startIdx = text.IndexOf('{');
        var endIdx = text.LastIndexOf('}');
        if (startIdx >= 0 && endIdx > startIdx) {
            return text.Substring(startIdx, endIdx - startIdx + 1);
        }
        return text;
    }
}
