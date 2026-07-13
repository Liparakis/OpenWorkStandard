using System.CommandLine;
using Ows.Cli.Commands;

namespace Ows.Cli;

/// <summary>
/// Builds the OWS command-line surface.
/// </summary>
public static class OwsCommandFactory {
    /// <summary>
    /// Builds the root command and local workflow commands.
    /// </summary>
    /// <returns>The configured root command.</returns>
    public static RootCommand BuildRootCommand() {
        var rootCommand = new RootCommand("Open Work Standard command-line interface") {
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

        if (msg.Contains("Watcher is already running", StringComparison.OrdinalIgnoreCase)) {
            return "Watcher is already running for this project.";
        }

        if (msg.Contains("Watcher has crashed", StringComparison.OrdinalIgnoreCase)) {
            return "Watcher has crashed or is not running.";
        }

        if (msg.Contains("database is locked", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("SQLite Error 5", StringComparison.OrdinalIgnoreCase)) {
            return "Local OWS database is locked by another process.";
        }

        return msg.Contains("Packaging failed", StringComparison.OrdinalIgnoreCase)
            ? "Packaging has crashed or is not running."
            : msg;
    }

    /// <summary>
    /// Prints the CLI response in either JSON or human-readable format.
    /// </summary>
    /// <param name="response">The CLI response to print.</param>
    /// <param name="useJson">Whether to format as JSON.</param>
    internal static void PrintResult(OwsCliResponse response, bool useJson) {
        if (useJson) {
            var json = System.Text.Json.JsonSerializer.Serialize(
                response.ToSerializableModel(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
            );
            Console.WriteLine(json);
        } else {
            if (response.Errors.Count > 0) {
                foreach (var err in response.Errors) {
                    Console.Error.WriteLine($"ERROR: {err}");
                }
            } else {
                if (!string.IsNullOrEmpty(response.Message)) {
                    Console.WriteLine(response.Message);
                }
            }
        }
    }
}
