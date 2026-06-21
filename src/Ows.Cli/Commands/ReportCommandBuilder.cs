using System.CommandLine;
using Ows.Core;
using Ows.Core.Reporting;
using Ows.Core.Verification;

namespace Ows.Cli.Commands;

/// <summary>
/// Provides construction for the report command.
/// </summary>
public static class ReportCommandBuilder {
    /// <summary>
    /// Builds the report command that generates an OWS verification report.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build() {
        var command = new Command("report", "Generate an OWS verification report.");
        var formatOption = new Option<string?>("--format") {
            Description = "Report output format. Supported values: text, json."
        };
        command.Options.Add(formatOption);
        command.SetAction(async parseResult => {
            var projectRoot = Directory.GetCurrentDirectory();
            var packagePath = Path.Combine(projectRoot,
                $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}");
            var verifier = new OwsPackageVerifier();
            var verificationResult = await verifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None);

            if (!verificationResult.IsSuccess) {
                Console.WriteLine(verificationResult.Summary);
                return 1;
            }

            var generator = new OwsReportGenerator();
            var format = (parseResult.GetValue(formatOption) ?? "text").Trim().ToLowerInvariant() switch {
                "text" => ReportFormat.Text,
                "json" => ReportFormat.Json,
                var unsupported => throw new ArgumentException(
                    $"Unsupported report format '{unsupported}'. Supported values: text, json.")
            };
            var reportResult = await generator.GenerateAsync(
                new ReportRequest {
                    Format = format,
                    VerificationResult = verificationResult
                },
                CancellationToken.None);

            var extension = format switch {
                ReportFormat.Text => "txt",
                ReportFormat.Json => "json",
                _ => throw new NotSupportedException($"Report format '{format}' is not supported by the CLI yet.")
            };
            var reportPath = Path.Combine(projectRoot, $"{new DirectoryInfo(projectRoot).Name}.report.{extension}");
            await File.WriteAllTextAsync(reportPath, reportResult.Content);
            Console.WriteLine($"OWS report created at {reportPath}");
            return 0;
        });

        return command;
    }
}
