using System.CommandLine;
using Ows.Agent;
using Ows.Packaging;
using Ows.Reporting;
using Ows.Verification;

namespace Ows.Cli;

/// <summary>
/// Builds the OWS command-line surface.
/// </summary>
public static class OwsCommandFactory
{
    /// <summary>
    /// Builds the root command and placeholder subcommands.
    /// </summary>
    /// <returns>The configured root command.</returns>
    public static RootCommand BuildRootCommand()
    {
        var rootCommand = new RootCommand("Open Work Standard command-line interface")
        {
            BuildInitCommand(),
            BuildWatchCommand(),
            BuildPackageCommand(),
            BuildVerifyCommand(),
            BuildReportCommand()
        };

        return rootCommand;
    }

    private static Command BuildInitCommand()
    {
        var command = new Command("init", "Initialize local OWS tracking metadata for a project.");
        command.SetAction(parseResult =>
        {
            _ = parseResult;
            Console.WriteLine("OWS init: not implemented yet");
            return Task.FromResult(0);
        });

        return command;
    }

    private static Command BuildWatchCommand()
    {
        var command = new Command("watch", "Start the local tracking agent skeleton.");
        command.SetAction(parseResult =>
        {
            _ = parseResult;
            _ = typeof(ITrackingAgent);
            Console.WriteLine("OWS watch: not implemented yet");
            return Task.FromResult(0);
        });

        return command;
    }

    private static Command BuildPackageCommand()
    {
        var command = new Command("package", "Create an OWS submission package.");
        command.SetAction(parseResult =>
        {
            _ = parseResult;
            _ = typeof(IPackageBuilder);
            Console.WriteLine("OWS package: not implemented yet");
            return Task.FromResult(0);
        });

        return command;
    }

    private static Command BuildVerifyCommand()
    {
        var command = new Command("verify", "Verify an OWS submission package.");
        command.SetAction(parseResult =>
        {
            _ = parseResult;
            _ = typeof(IPackageVerifier);
            Console.WriteLine("OWS verify: not implemented yet");
            return Task.FromResult(0);
        });

        return command;
    }

    private static Command BuildReportCommand()
    {
        var command = new Command("report", "Generate an OWS verification report.");
        command.SetAction(parseResult =>
        {
            _ = parseResult;
            _ = typeof(IReportGenerator);
            Console.WriteLine("OWS report: not implemented yet");
            return Task.FromResult(0);
        });

        return command;
    }
}
