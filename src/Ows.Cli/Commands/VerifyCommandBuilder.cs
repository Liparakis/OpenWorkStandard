using System.CommandLine;
using Ows.Core;
using Ows.Core.Verification;

namespace Ows.Cli.Commands;

/// <summary>
/// Provides construction for the offline verify command.
/// </summary>
public static class VerifyCommandBuilder {
    /// <summary>
    /// Builds the verify command that validates an OWS package locally.
    /// </summary>
    public static Command Build() {
        var command = new Command("verify", "Verify an OWS package offline.");
        var packageArgument = new Argument<string?>("package") {
            Description = "Path to the local .owspkg file; defaults to the current project package.",
            Arity = ArgumentArity.ZeroOrOne
        };
        command.Arguments.Add(packageArgument);
        command.SetAction(async parseResult => {
                var projectRoot = Directory.GetCurrentDirectory();
                var packagePath = parseResult.GetValue(packageArgument) ?? Path.Combine(
                    projectRoot,
                    $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}"
                );
                var result = await new OwsPackageVerifier().VerifyAsync(
                    new PackageVerificationRequest { PackagePath = packagePath }, CancellationToken.None
                );
                Console.WriteLine(result.Summary);
                return result.IsSuccess ? 0 : 1;
            }
        );

        return command;
    }
}
