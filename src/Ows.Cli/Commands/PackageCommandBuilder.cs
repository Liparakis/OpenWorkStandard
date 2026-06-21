using System.CommandLine;
using System.Text.Json;
using Ows.Core;
using Ows.Core.Agent;
using Ows.Core.Notarization;

namespace Ows.Cli.Commands;

/// <summary>
/// Provides construction for the package command group.
/// </summary>
public static class PackageCommandBuilder
{
    /// <summary>
    /// Builds the package command group to create and upload OWS packages.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build()
    {
        var command = new Command("package", "Create an OWS submission package.");

        var uploadCommand = new Command("upload", "Upload the package to a live verifier.");
        var packagePathOption = new Option<string?>("--package-path")
        {
            Description = "Path to the .owspkg file to upload."
        };
        var serverOption = new Option<string?>("--server")
        {
            Description = "Override the verifier base URL."
        };
        uploadCommand.Options.Add(packagePathOption);
        uploadCommand.Options.Add(serverOption);

        var statusCommand = new Command("status", "Query verification status of a package.");
        var packageIdOption = new Option<string?>("--package-id")
        {
            Description = "Durable package submission identifier."
        };
        statusCommand.Options.Add(packageIdOption);
        statusCommand.Options.Add(serverOption);

        command.Subcommands.Add(uploadCommand);
        command.Subcommands.Add(statusCommand);

        // Default action: Create package
        command.SetAction(async parseResult =>
        {
            var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
            var response = new OwsCliResponse();
            try
            {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(projectRoot))
                {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                var path = await manager.PackageProjectAsync(projectRoot);
                response.Success = true;
                response.ProjectRoot = projectRoot;
                response.Message = $"OWS package created successfully: {path}";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Errors.Add(OwsCommandFactory.GetFriendlyErrorMessage(ex));
            }

            OwsCommandFactory.PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        uploadCommand.SetAction(async parseResult =>
        {
            var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
            var pkgPath = parseResult.GetValue(packagePathOption);
            var serverUrl = parseResult.GetValue(serverOption);
            var response = new OwsCliResponse();
            try
            {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(projectRoot))
                {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                if (string.IsNullOrWhiteSpace(pkgPath))
                {
                    pkgPath = Path.Combine(projectRoot,
                        $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}");
                }

                if (!File.Exists(pkgPath))
                {
                    throw new FileNotFoundException($"Package file not found at: {pkgPath}");
                }

                var packageId = await manager.UploadPackageAsync(projectRoot, pkgPath, serverUrl);
                response.Success = true;
                response.PackageId = packageId;
                response.ProjectRoot = projectRoot;
                response.Message = $"Package uploaded successfully. Submission ID: {packageId}";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Errors.Add(OwsCommandFactory.GetFriendlyErrorMessage(ex));
            }

            OwsCommandFactory.PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        statusCommand.SetAction(async parseResult =>
        {
            var useJson = parseResult.GetValue(SharedCliOptions.JsonOption);
            var pkgId = parseResult.GetValue(packageIdOption);
            var serverUrl = parseResult.GetValue(serverOption);
            var response = new OwsCliResponse();
            try
            {
                var projectRoot = Directory.GetCurrentDirectory();
                var manager = new OwsWatchSessionManager();
                if (!manager.IsProjectInitialized(projectRoot))
                {
                    throw new InvalidOperationException("OWS project is not initialized. Run 'ows init' first.");
                }

                if (string.IsNullOrWhiteSpace(pkgId))
                {
                    var sessionPath = Path.Combine(projectRoot, OwsConstants.LocalFolderName,
                        OwsConstants.SessionFileName);
                    if (File.Exists(sessionPath))
                    {
                        var state = JsonSerializer.Deserialize<SessionState>(await File.ReadAllTextAsync(sessionPath));
                        pkgId = state?.LastPackageId;
                    }
                }

                if (string.IsNullOrWhiteSpace(pkgId))
                {
                    throw new InvalidOperationException(
                        "Package submission ID is required (no last package ID found in session).");
                }

                var jsonStatus = await manager.QueryPackageStatusAsync(projectRoot, pkgId, serverUrl);
                using var doc = JsonDocument.Parse(jsonStatus);
                var root = doc.RootElement;

                response.Success = true;
                response.PackageId = pkgId;
                response.Status = root.GetProperty("verificationStatus").GetString();
                response.TrustStatus = root.TryGetProperty("trustStatus", out var ts) ? ts.GetString() : null;
                response.Message =
                    $"Package verification status: {response.Status}. Trust status: {response.TrustStatus ?? "None"}";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Errors.Add(OwsCommandFactory.GetFriendlyErrorMessage(ex));
            }

            OwsCommandFactory.PrintResult(response, useJson);
            return response.Success ? 0 : 1;
        });

        return command;
    }
}