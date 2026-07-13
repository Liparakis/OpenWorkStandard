using System.CommandLine;
using Ows.Cli.Commands;

namespace Ows.Cli;

/// <summary>
/// Builds the OWS command-line surface.
/// </summary>
public static class OwsCommandFactory {
    /// <summary>
    /// Builds the root command and placeholder subcommands.
    /// </summary>
    /// <returns>The configured root command.</returns>
    public static RootCommand BuildRootCommand() {
        var rootCommand = new RootCommand("Open Work Standard command-line interface")
        {
            InitCommandBuilder.Build(),
            AgentCommandBuilder.Build(),
            StatusCommandBuilder.Build(),
            PackageCommandBuilder.Build(),
            VerifyCommandBuilder.Build(),
            InspectCommandBuilder.Build(),
            ReportCommandBuilder.Build()
        };

        rootCommand.Options.Add(SharedCliOptions.JsonOption);

        return rootCommand;
    }

    /// <summary>
    /// Converts an exception into a human-friendly error message.
    /// </summary>
    /// <param name="ex">The exception to process.</param>
    /// <returns>A friendly, descriptive error message.</returns>
    internal static string GetFriendlyErrorMessage(Exception ex) {
        var msg = ex.Message;

        if (ex is DirectoryNotFoundException) {
            return msg;
        }

        if (msg.Contains("not initialized", StringComparison.OrdinalIgnoreCase)) {
            return "OWS project is not initialized. Run 'ows init' first.";
        }

        if (msg.Contains("No remote verifier URL configured", StringComparison.OrdinalIgnoreCase)) {
            return "No remote verifier URL configured.";
        }

        if (msg.Contains("Assessment context is missing", StringComparison.OrdinalIgnoreCase) ||
            (msg.Contains("institutionId", StringComparison.OrdinalIgnoreCase) &&
             msg.Contains("required", StringComparison.OrdinalIgnoreCase))) {
            return
                "External context metadata is missing (Institution ID, Assessment ID, or Student User ID). Please configure the project context.";
        }

        if (msg.Contains("OWS_VERIFIER_API_KEY", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("API Key is missing", StringComparison.OrdinalIgnoreCase)) {
            return "No API key configured for remote verifier. Please set OWS_VERIFIER_API_KEY.";
        }

        if (ex is UnauthorizedAccessException || msg.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("forbidden", StringComparison.OrdinalIgnoreCase) || msg.Contains("401") ||
            msg.Contains("403") ||
            msg.Contains("rejected", StringComparison.OrdinalIgnoreCase)) {
            return "API key was rejected by the verifier. Please check your credentials.";
        }

        if (msg.Contains("Watcher is already running", StringComparison.OrdinalIgnoreCase)) {
            return "Watcher is already running for this project.";
        }

        if (msg.Contains("Watcher has crashed", StringComparison.OrdinalIgnoreCase)) {
            return "Watcher has crashed or is not running.";
        }

        if (ex is HttpRequestException ||
            msg.Contains("connection refused", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("unreachable", StringComparison.OrdinalIgnoreCase) || msg.Contains("503") ||
            msg.Contains("offline", StringComparison.OrdinalIgnoreCase)) {
            return "Verifier server is offline or unreachable.";
        }

        if (msg.Contains("database is locked", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("SQLite Error 5", StringComparison.OrdinalIgnoreCase)) {
            return "Local OWS database is locked by another process.";
        }

        if (msg.Contains("Packaging failed", StringComparison.OrdinalIgnoreCase)) {
            return msg;
        }

        if (msg.Contains("Upload failed", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("upload", StringComparison.OrdinalIgnoreCase)) {
            return $"Package upload failed: {msg}";
        }

        return msg;
    }

    /// <summary>
    /// Prints the CLI response in either JSON or human-readable format.
    /// </summary>
    /// <param name="response">The CLI response to print.</param>
    /// <param name="useJson">Whether to format as JSON.</param>
    internal static void PrintResult(OwsCliResponse response, bool useJson) {
        if (useJson) {
            var json = System.Text.Json.JsonSerializer.Serialize(response.ToSerializableModel(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(RedactApiKey(json));
        } else {
            if (response.Errors.Count > 0) {
                foreach (var err in response.Errors) {
                    Console.Error.WriteLine(RedactApiKey($"ERROR: {err}"));
                }
            } else {
                if (!string.IsNullOrEmpty(response.Message)) {
                    Console.WriteLine(RedactApiKey(response.Message));
                }
            }
        }
    }

    /// <summary>
    /// Redacts the OWS verifier API key from any output messages.
    /// </summary>
    /// <param name="input">The raw output string.</param>
    /// <returns>The redacted output string.</returns>
    private static string RedactApiKey(string input) {
        var apiKey = Environment.GetEnvironmentVariable("OWS_VERIFIER_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 6) {
            return input;
        }

        return input.Replace(apiKey, "[REDACTED_API_KEY]");
    }
}
