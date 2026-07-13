using System.CommandLine;
using Ows.Core;
using Ows.Core.Reporting;
using Ows.Core.Verification;

namespace Ows.Cli.Commands;

/// <summary>
///     Provides construction for the report command.
/// </summary>
public static class ReportCommandBuilder {
    /// <summary>
    ///     Builds the report command that generates an OWS verification report.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build() {
        var command = new Command("report", "Generate an OWS verification report.");
        var packageArgument = new Argument<string?>("package") {
            Description = "Path to the local .owspkg file; defaults to the current project package.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var formatOption = new Option<string?>("--format") {
            Description = "Report output format. Supported values: text, json."
        };
        command.Arguments.Add(packageArgument);
        command.Options.Add(formatOption);
        command.SetAction(async parseResult => {
                var projectRoot = Directory.GetCurrentDirectory();
                var packagePath = parseResult.GetValue(packageArgument) ?? Path.Combine(
                    projectRoot,
                    $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}"
                );
                var verifier = new OwsPackageVerifier();
                var verificationResult = await OwsPackageVerifier.VerifyAsync(
                    new PackageVerificationRequest { PackagePath = packagePath },
                    CancellationToken.None
                );

                if (!verificationResult.IsSuccess) {
                    Console.WriteLine(verificationResult.Summary);
                    return 1;
                }

                var generator = new OwsReportGenerator();
                var format = (parseResult.GetValue(formatOption) ?? "text").Trim().ToLowerInvariant() switch {
                    "text" => ReportFormat.Text,
                    "json" => ReportFormat.Json,
                    var unsupported => throw new ArgumentException(
                        $"Unsupported report format '{unsupported}'. Supported values: text, json."
                    )
                };
                var reportResult = await OwsReportGenerator.GenerateAsync(
                    new ReportRequest {
                        Format = format,
                        VerificationResult = verificationResult
                    },
                    CancellationToken.None
                );

                var extension = format switch {
                    ReportFormat.Text => "txt",
                    ReportFormat.Json => "json",
                    _ => throw new NotSupportedException($"Report format '{format}' is not supported by the CLI yet.")
                };
                var packageDirectory = Path.GetDirectoryName(Path.GetFullPath(packagePath)) ?? projectRoot;
                var packageName = Path.GetFileNameWithoutExtension(packagePath);
                var reportPath = Path.Combine(packageDirectory, $"{packageName}.report.{extension}");
                await File.WriteAllTextAsync(reportPath, reportResult.Content);
                Console.WriteLine($"OWS report created at {reportPath}");
                return 0;
            }
        );

        return command;
    }
}
