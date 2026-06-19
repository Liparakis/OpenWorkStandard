using System.CommandLine;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Ows.Core;
using Ows.Core.Agent;
using Ows.Core.Notarization;
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
            BuildSessionCommand(),
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
    /// Builds the command group for local session and checkpoint operations.
    /// </summary>
    /// <returns>The configured <c>session</c> command.</returns>
    private static Command BuildSessionCommand()
    {
        var command = new Command("session", "Manage local OWS session state.");
        command.Subcommands.Add(BuildSessionStartCommand());
        command.Subcommands.Add(BuildCheckpointCommand());
        return command;
    }

    /// <summary>
    /// Builds the command for starting a local session.
    /// </summary>
    /// <returns>The configured <c>session start</c> command.</returns>
    private static Command BuildSessionStartCommand()
    {
        var command = new Command("start", "Start a local OWS assessment session.");
        var serverOption = new Option<string?>("--server")
        {
            Description = "Use a remote verifier base URL for receipt issuance."
        };
        command.Options.Add(serverOption);
        command.SetAction(async parseResult =>
        {
            var sessionId = await OwsSessionStore.StartSessionAsync(
                Directory.GetCurrentDirectory(),
                parseResult.GetValue(serverOption),
                CancellationToken.None);
            Console.WriteLine($"OWS session started: {sessionId}");
            return 0;
        });

        return command;
    }

    /// <summary>
    /// Builds the command for checkpointing the current timeline head locally.
    /// </summary>
    /// <returns>The configured <c>checkpoint</c> command.</returns>
    private static Command BuildCheckpointCommand()
    {
        var command = new Command("checkpoint", "Issue a local receipt for the current timeline head.");
        command.SetAction(async parseResult =>
        {
            _ = parseResult;
            var receipt = await OwsSessionStore.AddCheckpointAsync(Directory.GetCurrentDirectory(), CancellationToken.None);
            Console.WriteLine($"OWS checkpoint recorded: {receipt.ReceiptHash}");
            return 0;
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
        var serverOption = new Option<string?>("--server")
        {
            Description = "Cross-check packaged receipts against a live verifier API."
        };
        command.Options.Add(serverOption);
        command.SetAction(async parseResult =>
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var packagePath = Path.Combine(projectRoot, $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}");
            var verifier = new OwsPackageVerifier();
            var verifierUrl = parseResult.GetValue(serverOption);
            var trustedReceiptChain = string.IsNullOrWhiteSpace(verifierUrl)
                ? null
                : await FetchTrustedReceiptChainAsync(packagePath, verifierUrl, CancellationToken.None);
            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest
                {
                    PackagePath = packagePath,
                    TrustedReceiptChain = trustedReceiptChain
                },
                CancellationToken.None);
            Console.WriteLine(result.Summary);
            return result.IsSuccess ? 0 : 1;
        });

        return command;
    }

    /// <summary>
    /// Fetches the authoritative receipt chain for the packaged session from a live verifier.
    /// </summary>
    /// <param name="packagePath">The package being verified.</param>
    /// <param name="verifierUrl">The verifier base URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The trusted remote receipt chain.</returns>
    private static async Task<ReceiptChain> FetchTrustedReceiptChainAsync(
        string packagePath,
        string verifierUrl,
        CancellationToken cancellationToken)
    {
        var sessionId = ReadPackagedSessionId(packagePath);
        var packagedReceiptChain = ReadPackagedReceiptChain(packagePath);
        if (sessionId is null && packagedReceiptChain is null)
        {
            throw new InvalidOperationException(
                $"The package does not contain {OwsConstants.SessionFileName} or {OwsConstants.ReceiptsFileName}, so verifier-backed verification cannot resolve a remote session.");
        }

        using var httpClient = new HttpClient { BaseAddress = new Uri(verifierUrl, UriKind.Absolute) };
        var transport = new HttpsReceiptTransport(httpClient, (_, _) => new Checkpoint());
        transport.RestoreSession(
            sessionId ?? packagedReceiptChain!.SessionId,
            packagedReceiptChain?.Receipts.Count + 1 ?? 1);
        return await transport.GetReceiptsAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the packaged receipt chain from the target package when present.
    /// </summary>
    /// <param name="packagePath">The package path.</param>
    /// <returns>The packaged receipt chain when present; otherwise <see langword="null"/>.</returns>
    private static ReceiptChain? ReadPackagedReceiptChain(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var receiptsEntry = archive.GetEntry(OwsConstants.ReceiptsFileName);
        if (receiptsEntry is null)
        {
            return null;
        }

        using var reader = new StreamReader(receiptsEntry.Open());
        return JsonSerializer.Deserialize<ReceiptChain>(reader.ReadToEnd());
    }

    /// <summary>
    /// Reads the packaged session identifier from session metadata when present.
    /// </summary>
    /// <param name="packagePath">The package path.</param>
    /// <returns>The packaged session identifier when present; otherwise <see langword="null"/>.</returns>
    private static AssessmentSessionId? ReadPackagedSessionId(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var sessionEntry = archive.GetEntry(OwsConstants.SessionFileName);
        if (sessionEntry is null)
        {
            return null;
        }

        using var reader = new StreamReader(sessionEntry.Open());
        var sessionState = JsonSerializer.Deserialize<SessionState>(reader.ReadToEnd())
            ?? throw new JsonException("Session state deserialized to null.");
        return string.IsNullOrWhiteSpace(sessionState.SessionId)
            ? null
            : new AssessmentSessionId(sessionState.SessionId);
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
            await File.WriteAllTextAsync(reportPath, reportResult.Content);
            Console.WriteLine($"OWS report created at {reportPath}");
            return 0;
        });

        return command;
    }
}
