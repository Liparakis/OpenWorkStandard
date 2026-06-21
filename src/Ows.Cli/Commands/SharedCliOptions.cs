using System.CommandLine;

namespace Ows.Cli.Commands;

/// <summary>
/// Defines options that are shared across multiple OWS CLI commands.
/// </summary>
public static class SharedCliOptions {
    /// <summary>
    /// Gets the shared option for outputting command results in JSON format.
    /// </summary>
    public static readonly Option<bool> JsonOption = new("--json") {
        Description = "Output result in JSON format.",
        Recursive = true
    };
}
