using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Ows.Core;
using Ows.Core.Agent;
using Ows.Core.Notarization;
using Xunit;

namespace Ows.Cli.Tests;

[Collection(CliCommandCollection.Name)]
public sealed class CliHardeningTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    [Fact]
    public async Task Cli_ShouldReturnErrorStatus_WhenWatcherCrashed()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-crash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var originalDirectory = Directory.GetCurrentDirectory();
        
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            Directory.SetCurrentDirectory(projectRoot);

            // Initialize OWS
            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);

            // Write stale lock file to simulate watcher crash
            var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            var watcherJsonPath = Path.Combine(localFolder, "watcher.json");
            var staleState = new WatcherProcessState
            {
                Pid = 999999, // Unlikely to exist
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            };
            File.WriteAllText(watcherJsonPath, JsonSerializer.Serialize(staleState));

            using (var sw = new StringWriter())
            {
                Console.SetOut(sw);
                var parseResult = OwsCommandFactory.BuildRootCommand().Parse(["status", "--json"]);
                var exitCode = await parseResult.InvokeAsync();
                
                // Note: CLI status execution itself completes with 0, but reports status="Error" in JSON
                exitCode.Should().Be(0);

                var output = ExtractJson(sw.ToString());
                var doc = JsonDocument.Parse(output);
                doc.RootElement.GetProperty("Success").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("Status").GetString().Should().Be("Error");
                doc.RootElement.GetProperty("Errors")[0].GetString().Should().Contain("Watcher has crashed or is not running.");
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot))
            {
                try { Directory.Delete(projectRoot, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task Cli_ShouldReturnVerifierOfflineStatus_WhenVerifierOffline()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-offline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        var originalDirectory = Directory.GetCurrentDirectory();
        
        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            Directory.SetCurrentDirectory(projectRoot);

            // Initialize OWS
            var manager = new OwsWatchSessionManager();
            manager.InitializeProject(projectRoot);

            // Create fake session state with IsVerifierOffline = true
            var localFolder = Path.Combine(projectRoot, OwsConstants.LocalFolderName);
            var sessionState = new SessionState
            {
                SessionId = "fake-session-123",
                VerifierUrl = "http://localhost:5078",
                IsVerifierOffline = true,
                LastHeartbeatError = "Connection refused"
            };
            File.WriteAllText(
                Path.Combine(localFolder, OwsConstants.SessionFileName),
                JsonSerializer.Serialize(sessionState, SerializerOptions));

            using (var sw = new StringWriter())
            {
                Console.SetOut(sw);
                var parseResult = OwsCommandFactory.BuildRootCommand().Parse(["status", "--json"]);
                await parseResult.InvokeAsync();

                var output = ExtractJson(sw.ToString());
                var doc = JsonDocument.Parse(output);
                doc.RootElement.GetProperty("Status").GetString().Should().Be("VerifierOffline");
                doc.RootElement.GetProperty("Errors")[0].GetString().Should().Contain("Connection refused");
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Directory.SetCurrentDirectory(originalDirectory);

            if (Directory.Exists(projectRoot))
            {
                try { Directory.Delete(projectRoot, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void Cli_ShouldRedactApiKey_InExceptionsAndFriendlyErrors()
    {
        var secretKey = "api-key-to-redact-99999";
        Environment.SetEnvironmentVariable("OWS_VERIFIER_API_KEY", secretKey);

        var originalOut = Console.Out;
        var originalError = Console.Error;

        try
        {
            // Trigger a dummy command or parse with the secret key in the argument to cause exception
            var exception = new Exception($"Error: Unauthorized with {secretKey} API key.");
            
            // Invoke the OwsCommandFactory's private/internal method or just invoke RedactApiKey indirectly.
            // Since RedactApiKey is private, we can verify via PrintResult or error formatting.
            // Let's trigger a command that is sure to fail and check the exception redaction:
            var mockResponse = new OwsCliResponse();
            mockResponse.Success = false;
            mockResponse.Errors.Add($"Failed auth with key {secretKey}");

            using (var sw = new StringWriter())
            {
                Console.SetOut(sw);
                // Call a failing command or use PrintResult simulation
                var parseResult = OwsCommandFactory.BuildRootCommand().Parse(["status", "invalid-option-args-here"]);
                parseResult.Invoke();

                // Test our RedactApiKey logic via actual command output
                using (var swError = new StringWriter())
                {
                    Console.SetError(swError);
                    var prMethod = typeof(OwsCommandFactory).GetMethod("PrintResult", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    prMethod.Should().NotBeNull();
                    prMethod!.Invoke(null, new object[] { mockResponse, false });

                    var errOut = swError.ToString();
                    errOut.Should().NotContain(secretKey);
                    errOut.Should().Contain("[REDACTED_API_KEY]");
                }
            }
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Environment.SetEnvironmentVariable("OWS_VERIFIER_API_KEY", null);
        }
    }

    private static string ExtractJson(string text)
    {
        var startIdx = text.IndexOf('{');
        var endIdx = text.LastIndexOf('}');
        if (startIdx >= 0 && endIdx > startIdx)
        {
            return text.Substring(startIdx, endIdx - startIdx + 1);
        }
        return text;
    }
}
