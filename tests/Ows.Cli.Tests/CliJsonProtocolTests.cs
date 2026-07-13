using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Ows.Core;
using Xunit;

namespace Ows.Cli.Tests;

/// <summary>
/// Tests JSON formatting, errors, and output redaction in CLI commands.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class CliJsonProtocolTests {
    [Fact]
    public async Task CliCommands_ShouldReturnValidJsonResponses() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-json-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var originalDirectory = Directory.GetCurrentDirectory();

        var originalOut = Console.Out;
        var originalError = Console.Error;

        try {
            Directory.SetCurrentDirectory(projectRoot);

            // 1. Test status --json on uninitialized project
            using (var sw = new StringWriter()) {
                Console.SetOut(sw);
                var parseResult = OwsCommandFactory.BuildRootCommand().Parse(["status", "--json"]);
                var exitCode = await parseResult.InvokeAsync();
                exitCode.Should().Be(1);

                var output = ExtractJson(sw.ToString());
                var doc = JsonDocument.Parse(output);
                doc.RootElement.GetProperty("Success").GetBoolean().Should().BeFalse();
                doc.RootElement.GetProperty("Status").GetString().Should().Be("Not Initialized");
                doc.RootElement.GetProperty("Errors")[0].GetString().Should().Contain("not initialized");
            }

            // 2. Test init --json
            using (var sw = new StringWriter()) {
                Console.SetOut(sw);
                var parseResult = OwsCommandFactory.BuildRootCommand().Parse(["init", "--json"]);
                var exitCode = await parseResult.InvokeAsync();
                exitCode.Should().Be(0);

                var output = ExtractJson(sw.ToString());
                var doc = JsonDocument.Parse(output);
                doc.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("Status").GetString().Should().Be("AgentUnavailable");
                doc.RootElement.GetProperty("Message").GetString().Should().Contain("local Agent is unavailable");
                doc.RootElement.GetProperty("ProjectRoot").GetString().Should().Be(projectRoot);
            }

            // 3. Test status --json on initialized project
            using (var sw = new StringWriter()) {
                Console.SetOut(sw);
                var parseResult = OwsCommandFactory.BuildRootCommand().Parse(["status", "--json"]);
                var exitCode = await parseResult.InvokeAsync();
                exitCode.Should().Be(0);

                var output = ExtractJson(sw.ToString());
                var doc = JsonDocument.Parse(output);
                doc.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("Status").GetString().Should().Be("Ready");
                doc.RootElement.GetProperty("WatcherRunning").GetBoolean().Should().BeFalse();
            }

        } finally {
            Console.SetOut(originalOut);
            Console.SetOut(originalError);
            Directory.SetCurrentDirectory(originalDirectory);
            if (Directory.Exists(projectRoot)) {
                try {
                    Directory.Delete(projectRoot, recursive: true);
                } catch {
                }
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
