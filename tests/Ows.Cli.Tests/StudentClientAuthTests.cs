using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Ows.Verifier.Server;
using Xunit;

namespace Ows.Cli.Tests;

/// <summary>
/// Integration tests for StudentClient scope enforcement over opaque project metadata.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class StudentClientAuthTests {
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
    public async Task StudentClient_ShouldStayWithinBoundProjectScope() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-student-client-{Guid.NewGuid():N}");
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
                var studentApiKey = await CreateApiKeyAsync(client, adminApiKey,
                    new { role = "StudentClient", institutionId = "inst-1", studentUserId = "student-1" });

                using var startRequest = new HttpRequestMessage(HttpMethod.Post, "/sessions") {
                    Content = JsonContent.Create(new { institutionId = "inst-1", studentUserId = "student-1" })
                };
                startRequest.Headers.Add("X-OWS-Verifier-Key", studentApiKey);
                var startResponse = await client.SendAsync(startRequest);
                startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                using var startDocument = JsonDocument.Parse(await startResponse.Content.ReadAsStringAsync());
                var sessionId = startDocument.RootElement.GetProperty("sessionId").GetString();

                using var wrongStudentRequest = new HttpRequestMessage(HttpMethod.Post, "/sessions") {
                    Content = JsonContent.Create(new { institutionId = "inst-1", studentUserId = "student-2" })
                };
                wrongStudentRequest.Headers.Add("X-OWS-Verifier-Key", studentApiKey);
                (await client.SendAsync(wrongStudentRequest)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

                using var wrongInstitutionRequest = new HttpRequestMessage(HttpMethod.Post, "/sessions") {
                    Content = JsonContent.Create(new { institutionId = "inst-2", studentUserId = "student-1" })
                };
                wrongInstitutionRequest.Headers.Add("X-OWS-Verifier-Key", studentApiKey);
                (await client.SendAsync(wrongInstitutionRequest)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

                using var heartbeatRequest = new HttpRequestMessage(HttpMethod.Post,
                    $"/sessions/{sessionId}/heartbeat") {
                    Content = JsonContent.Create(new { lastKnownEventHash = "abc" })
                };
                heartbeatRequest.Headers.Add("X-OWS-Verifier-Key", studentApiKey);
                (await client.SendAsync(heartbeatRequest)).StatusCode.Should().Be(HttpStatusCode.OK);

                using var checkpointRequest = new HttpRequestMessage(HttpMethod.Post,
                    $"/sessions/{sessionId}/checkpoints") {
                    Content = JsonContent.Create(new {
                        sessionId,
                        sequenceNumber = 1,
                        timelineHeadHash = "def"
                    })
                };
                checkpointRequest.Headers.Add("X-OWS-Verifier-Key", studentApiKey);
                (await client.SendAsync(checkpointRequest)).StatusCode.Should().Be(HttpStatusCode.OK);
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
