using System.CommandLine;
using Microsoft.Extensions.Logging.Abstractions;
using Ows.Core;
using Ows.Core.Agent;
using Ows.Core.Init;
using Ows.Core.Packaging;
using Ows.Core.Reporting;
using Ows.Core.Verification;

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

    /// <summary>
    /// Builds the placeholder command for project initialization.
    /// </summary>
    /// <returns>The configured <c>init</c> command.</returns>
    private static Command BuildInitCommand()
    {
        var command = new Command("init", "Initialize local OWS tracking metadata for a project.");
        command.SetAction(parseResult =>
        {
            _ = parseResult;
            var initializer = new OwsProjectInitializer();
            var result = initializer.Initialize(Directory.GetCurrentDirectory());
            Console.WriteLine($"OWS initialized at {result.LocalFolderPath}");
            return Task.FromResult(0);
        });

        return command;
    }

    /// <summary>
    /// Builds the placeholder command for starting local tracking.
    /// </summary>
    /// <returns>The configured <c>watch</c> command.</returns>
    private static Command BuildWatchCommand()
    {
        var command = new Command("watch", "Start the local tracking agent skeleton.");
        command.SetAction(async parseResult =>
        {
            _ = parseResult;
            var projectRoot = Directory.GetCurrentDirectory();
            var agent = new LocalTrackingAgent(new NullLogger<LocalTrackingAgent>());
            await agent.PrepareAsync(
                new TrackingAgentOptions
                {
                    ProjectRootPath = projectRoot,
                    DatabasePath = Path.Combine(projectRoot, OwsConstants.LocalFolderName, "ows.db")
                },
                CancellationToken.None);
            var result = await agent.StartAsync(CancellationToken.None);
            Console.WriteLine(result.Message);
            return 0;
        });

        return command;
    }

    /// <summary>
    /// Builds the placeholder command for package creation.
    /// </summary>
    /// <returns>The configured <c>package</c> command.</returns>
    private static Command BuildPackageCommand()
    {
        var command = new Command("package", "Create an OWS submission package.");
        command.SetAction(async parseResult =>
        {
            _ = parseResult;
            var projectRoot = Directory.GetCurrentDirectory();
            var packagePath = Path.Combine(projectRoot, $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}");
            var builder = new OwsPackageBuilder();
            var result = await builder.CreatePackageAsync(
                new PackageCreationRequest
                {
                    ProjectRootPath = projectRoot,
                    OutputPackagePath = packagePath
                },
                CancellationToken.None);
            Console.WriteLine(result.Message);
            return 0;
        });

        return command;
    }

    /// <summary>
    /// Builds the placeholder command for package verification.
    /// </summary>
    /// <returns>The configured <c>verify</c> command.</returns>
    private static Command BuildVerifyCommand()
    {
        var command = new Command("verify", "Verify an OWS submission package.");
        command.SetAction(async parseResult =>
        {
            _ = parseResult;
            var projectRoot = Directory.GetCurrentDirectory();
            var packagePath = Path.Combine(projectRoot, $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}");
            var verifier = new OwsPackageVerifier();
            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);
            Console.WriteLine(result.Summary);
            return result.IsSuccess ? 0 : 1;
        });

        return command;
    }

    /// <summary>
    /// Builds the placeholder command for report generation.
    /// </summary>
    /// <returns>The configured <c>report</c> command.</returns>
    private static Command BuildReportCommand()
    {
        var command = new Command("report", "Generate an OWS verification report.");
        command.SetAction(async parseResult =>
        {
            _ = parseResult;
            var projectRoot = Directory.GetCurrentDirectory();
            var packagePath = Path.Combine(projectRoot, $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}");
            var verifier = new OwsPackageVerifier();
            var verificationResult = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            if (!verificationResult.IsSuccess)
            {
                Console.WriteLine(verificationResult.Summary);
                return 1;
            }

            var generator = new OwsReportGenerator();
            var reportResult = await generator.GenerateAsync(
                new ReportRequest
                {
                    Format = ReportFormat.Text,
                    VerificationResult = verificationResult
                },
                CancellationToken.None);

            var reportPath = Path.Combine(projectRoot, $"{new DirectoryInfo(projectRoot).Name}.report.txt");
            File.WriteAllText(reportPath, reportResult.Content);
            Console.WriteLine($"OWS report created at {reportPath}");
            return 0;
        });

        return command;
    }
}
