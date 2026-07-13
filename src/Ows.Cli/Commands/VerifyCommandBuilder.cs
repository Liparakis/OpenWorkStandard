using System.CommandLine;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using Ows.Core;
using Ows.Core.Notarization;
using Ows.Core.Verification;

namespace Ows.Cli.Commands;

/// <summary>
/// Provides construction for the verify command.
/// </summary>
public static class VerifyCommandBuilder {
    /// <summary>
    /// Builds the verify command that verifies an OWS submission package.
    /// </summary>
    /// <returns>The configured command.</returns>
    public static Command Build() {
        var command = new Command("verify", "Verify an OWS submission package.");
        var packageArgument = new Argument<string?>("package") {
            Description = "Path to the local .owspkg file; defaults to the current project package.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var serverOption = new Option<string?>("--server") {
            Description = "Cross-check packaged receipts against a live verifier API."
        };
        command.Arguments.Add(packageArgument);
        command.Options.Add(serverOption);
        command.SetAction(async parseResult => {
            var projectRoot = Directory.GetCurrentDirectory();
            var packagePath = parseResult.GetValue(packageArgument) ?? Path.Combine(projectRoot,
                $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}");
            var verifier = new OwsPackageVerifier();
            var verifierUrl = parseResult.GetValue(serverOption);
            var packagedReceiptChain = ReadPackagedReceiptChain(packagePath);
            var trustedReceiptChain = string.IsNullOrWhiteSpace(verifierUrl)
                ? null
                : await FetchTrustedReceiptChainAsync(packagePath, verifierUrl, packagedReceiptChain is not null,
                    CancellationToken.None);
            var trustedSessionHead = string.IsNullOrWhiteSpace(verifierUrl)
                ? null
                : await FetchTrustedSessionHeadAsync(packagePath, verifierUrl, packagedReceiptChain is null,
                    CancellationToken.None);
            var result = await verifier.VerifyAsync(
                new PackageVerificationRequest {
                    PackagePath = packagePath,
                    TrustedReceiptChain = trustedReceiptChain,
                    TrustedSessionHead = trustedSessionHead
                },
                CancellationToken.None);
            Console.WriteLine(result.Summary);
            return result.IsSuccess ? 0 : 1;
        });

        return command;
    }

    /// <summary>
    /// Fetches the trusted receipt chain from the verifier.
    /// </summary>
    /// <param name="packagePath">The path to the package.</param>
    /// <param name="verifierUrl">The verifier base URL.</param>
    /// <param name="shouldFetchChain">Whether to fetch the chain.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The fetched receipt chain, or null.</returns>
    private static async Task<ReceiptChain?> FetchTrustedReceiptChainAsync(
        string packagePath,
        string verifierUrl,
        bool shouldFetchChain,
        CancellationToken cancellationToken) {
        if (!shouldFetchChain) {
            return null;
        }

        var sessionId = ReadPackagedSessionId(packagePath);
        var packagedReceiptChain = ReadPackagedReceiptChain(packagePath);
        if (sessionId is null && packagedReceiptChain is null) {
            throw new InvalidOperationException(
                $"The package does not contain {OwsConstants.SessionFileName} or {OwsConstants.ReceiptsFileName}, so verifier-backed verification cannot resolve a remote session.");
        }

        using var httpClient = CreateVerifierHttpClient(verifierUrl);
        var transport = new HttpsReceiptTransport(httpClient, (_, _) => new Checkpoint());
        transport.RestoreSession(
            sessionId ?? packagedReceiptChain!.SessionId,
            packagedReceiptChain!.Receipts.Count + 1);
        return await transport.GetReceiptsAsync(cancellationToken);
    }

    /// <summary>
    /// Fetches the trusted session head from the verifier.
    /// </summary>
    /// <param name="packagePath">The path to the package.</param>
    /// <param name="verifierUrl">The verifier base URL.</param>
    /// <param name="shouldFetchHead">Whether to fetch the head.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The fetched session head response, or null.</returns>
    private static async Task<SessionHeadResponse?> FetchTrustedSessionHeadAsync(
        string packagePath,
        string verifierUrl,
        bool shouldFetchHead,
        CancellationToken cancellationToken) {
        if (!shouldFetchHead) {
            return null;
        }

        var sessionId = ReadPackagedSessionId(packagePath)
                        ?? throw new InvalidOperationException(
                            $"The package does not contain {OwsConstants.SessionFileName}, so verifier-backed verification cannot resolve a remote session head.");
        using var httpClient = CreateVerifierHttpClient(verifierUrl);
        return await httpClient.GetFromJsonAsync<SessionHeadResponse>($"sessions/{sessionId.Value}/head",
                   cancellationToken)
               ?? throw new InvalidOperationException("The verifier returned an invalid session head response.");
    }

    /// <summary>
    /// Creates an HttpClient configured for the verifier server.
    /// </summary>
    /// <param name="verifierUrl">The verifier base URL.</param>
    /// <returns>The configured HttpClient.</returns>
    private static HttpClient CreateVerifierHttpClient(string verifierUrl) {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifierUrl);
        var httpClient = new HttpClient { BaseAddress = new Uri(verifierUrl, UriKind.Absolute) };
        var apiKey = Environment.GetEnvironmentVariable("OWS_VERIFIER_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey)) {
            httpClient.DefaultRequestHeaders.Add("X-OWS-Verifier-Key", apiKey);
        }

        return httpClient;
    }

    /// <summary>
    /// Reads the receipt chain packaged inside the submission package.
    /// </summary>
    /// <param name="packagePath">The path to the package.</param>
    /// <returns>The receipt chain, or null if not found.</returns>
    private static ReceiptChain? ReadPackagedReceiptChain(string packagePath) {
        using var archive = ZipFile.OpenRead(packagePath);
        var receiptsEntry = archive.GetEntry(OwsConstants.ReceiptsFileName);
        if (receiptsEntry is null) {
            return null;
        }

        using var reader = new StreamReader(receiptsEntry.Open());
        return JsonSerializer.Deserialize<ReceiptChain>(reader.ReadToEnd());
    }

    /// <summary>
    /// Reads the session ID packaged inside the submission package.
    /// </summary>
    /// <param name="packagePath">The path to the package.</param>
    /// <returns>The assessment session ID, or null if not found.</returns>
    private static AssessmentSessionId? ReadPackagedSessionId(string packagePath) {
        using var archive = ZipFile.OpenRead(packagePath);
        var sessionEntry = archive.GetEntry(OwsConstants.SessionFileName);
        if (sessionEntry is null) {
            return null;
        }

        using var reader = new StreamReader(sessionEntry.Open());
        var sessionState = JsonSerializer.Deserialize<SessionState>(reader.ReadToEnd())
                           ?? throw new JsonException("Session state deserialized to null.");
        return string.IsNullOrWhiteSpace(sessionState.SessionId)
            ? null
            : new AssessmentSessionId(sessionState.SessionId);
    }
}
