using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Ows.Verifier.Server;
using Xunit;

namespace Ows.Cli.Tests;

/// <summary>
/// Integration tests for institution-scoped verifier authorization over opaque project metadata.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class InstitutionAdminAuthTests {
    private static readonly Lock EnvLock = new();

    private static HttpClient CreateClientWithEnv(Dictionary<string, string?> envVars,
        out WebApplicationFactory<global::Program> factory) {
        lock (EnvLock) {
            foreach (var (key, value) in envVars) {
                Environment.SetEnvironmentVariable(key, value);
            }

            try {
                factory = new WebApplicationFactory<global::Program>();
                return factory.CreateClient();
            } finally {
                foreach (var key in envVars.Keys) {
                    Environment.SetEnvironmentVariable(key, null);
                }
            }
        }
    }

    [Fact]
    public async Task InstitutionAdmin_ShouldScopeDelegatedKeysSessionsAndAudit() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-inst-admin-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try {
            var config = new Dictionary<string, string?> {
                ["VerifierEnvironment"] = "Local",
                ["VerifierStorage__Provider"] = "json",
                ["VerifierStorage__JsonStorePath"] = tempDbPath,
                ["VerifierStorage__LocalStoragePath"] = Path.Combine(tempDbDir, "packages"),
                ["VerifierSecurity__ApiKey"] = "bootstrap-operator-key-1234"
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory) {
                var adminApiKey = await CreateApiKeyAsync(client, "bootstrap-operator-key-1234",
                    new { role = "InstitutionAdmin", institutionId = "inst-1" });
                var reviewerApiKey = await CreateApiKeyAsync(client, adminApiKey,
                    new { role = "InstructorReviewer", institutionId = "inst-1" });

                using var operatorKeyRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys") {
                    Content = JsonContent.Create(new { role = "Operator" })
                };
                operatorKeyRequest.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                (await client.SendAsync(operatorKeyRequest)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

                using var crossInstitutionKeyRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys") {
                    Content = JsonContent.Create(new { role = "InstructorReviewer", institutionId = "inst-2" })
                };
                crossInstitutionKeyRequest.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                (await client.SendAsync(crossInstitutionKeyRequest)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

                using var ownSessionRequest = new HttpRequestMessage(HttpMethod.Post, "/sessions") {
                    Content = JsonContent.Create(new {
                        institutionId = "inst-1",
                        assessmentId = "a-1",
                        courseOfferingId = "offering-1"
                    })
                };
                ownSessionRequest.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                (await client.SendAsync(ownSessionRequest)).StatusCode.Should().Be(HttpStatusCode.OK);

                using var otherSessionRequest = new HttpRequestMessage(HttpMethod.Post, "/sessions") {
                    Content = JsonContent.Create(new { institutionId = "inst-2", assessmentId = "a-2" })
                };
                otherSessionRequest.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                (await client.SendAsync(otherSessionRequest)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

                using var operatorSessionRequest = new HttpRequestMessage(HttpMethod.Post, "/sessions") {
                    Content = JsonContent.Create(new { institutionId = "inst-2" })
                };
                operatorSessionRequest.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                (await client.SendAsync(operatorSessionRequest)).StatusCode.Should().Be(HttpStatusCode.OK);

                using var auditRequest = new HttpRequestMessage(HttpMethod.Get, "/audit/events");
                auditRequest.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                var auditResponse = await client.SendAsync(auditRequest);
                auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var auditEvents = await auditResponse.Content.ReadFromJsonAsync<List<VerifierAuditEvent>>();
                auditEvents.Should().NotBeNull();
                auditEvents!.Any(auditEvent => auditEvent.InstitutionId == "inst-2").Should().BeFalse();

                using var reviewerWriteRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys") {
                    Content = JsonContent.Create(new { role = "Operator" })
                };
                reviewerWriteRequest.Headers.Add("X-OWS-Verifier-Key", reviewerApiKey);
                (await client.SendAsync(reviewerWriteRequest)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            }
        } finally {
            if (Directory.Exists(tempDbDir)) {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    private static async Task<string> CreateApiKeyAsync(HttpClient client, string parentKey, object payload) {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys") {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("X-OWS-Verifier-Key", parentKey);
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("apiKey").GetString()!;
    }
}
