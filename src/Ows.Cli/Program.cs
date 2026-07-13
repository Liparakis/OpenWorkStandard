namespace Ows.Cli;

/// <summary>
///     Entry point for the OWS command-line interface.
/// </summary>
public static class Program {
    /// <summary>
    ///     Runs the OWS command-line interface.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> Main(string[] args) {
        var rootCommand = OwsCommandFactory.BuildRootCommand();
        var parseResult = rootCommand.Parse(args.Length == 0 ? ["--help"] : args);
        return await parseResult.InvokeAsync();
    }
}
