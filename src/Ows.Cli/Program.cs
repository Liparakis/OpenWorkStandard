using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ows.Cli;

/// <summary>
/// Entry point for the OWS command-line interface.
/// </summary>
public static class Program {
    /// <summary>
    /// Runs the OWS command-line interface.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The process exit code.</returns>
    public static async Task<int> Main(string[] args) {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddSimpleConsole();

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OWS");
        logger.LogInformation("Starting OWS CLI.");

        var parseResult = OwsCommandFactory.BuildRootCommand().Parse(args);
        return await parseResult.InvokeAsync();
    }
}
