using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Ows.Core.Education;
using Ows.Core.Notarization;
using Ows.Core.Verification;
using Ows.Verifier.Server;

namespace Ows.Cli.Tests;

/// <summary>
/// Integration tests for the OWS Verifier Server endpoints, API key guard, and startup validations.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class VerifierServerTests
{
    private static readonly Lock EnvLock = new();

    /// <summary>
    /// Helper to create a test client and factory with environment variables configured before host building.
    /// </summary>
    private static HttpClient CreateClientWithEnv(Dictionary<string, string?> envVars,
        out WebApplicationFactory<global::Program> factory)
    {
        lock (EnvLock)
        {
            foreach (var (k, v) in envVars)
            {
                Environment.SetEnvironmentVariable(k, v);
            }

            try
            {
                factory = new WebApplicationFactory<global::Program>();
                return factory.CreateClient();
            }
            finally
            {
                foreach (var k in envVars.Keys)
                {
                    Environment.SetEnvironmentVariable(k, null);
                }
            }
        }
    }

    private static async Task<string> CreateTestPackageAsync(string projectRoot)
    {
        Directory.CreateDirectory(projectRoot);
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "draft.txt"), "draft content");
        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(projectRoot);
            await OwsCommandFactory.BuildRootCommand().Parse(["init"]).InvokeAsync();
            await OwsTestHelpers.RunInitialScanAsync(projectRoot);
            await OwsCommandFactory.BuildRootCommand().Parse(["package"]).InvokeAsync();
            var packagePath = Path.Combine(projectRoot, $"{new DirectoryInfo(projectRoot).Name}.owspkg");
            return packagePath;
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    /// <summary>
    /// Polls the package status endpoint until one of the expected statuses is observed.
    /// </summary>
    private static async Task<JsonElement> WaitForPackageStatusAsync(
        HttpClient client,
        string submissionId,
        string? apiKey = null,
        params string[] expectedStatuses)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/packages/{submissionId}");
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Add("X-OWS-Verifier-Key", apiKey);
            }

            var response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement.Clone();
            var status = root.GetProperty("verificationStatus").GetString();
            if (expectedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                return root;
            }

            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException(
            $"Package '{submissionId}' never reached one of the expected statuses: {string.Join(", ", expectedStatuses)}.");
    }

    /// <summary>
    /// Verifies that GET /health returns 200 OK and "Healthy".
    /// </summary>
    [Fact]
    public async Task GetHealth_ShouldReturn200AndHealthy()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKey", "" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                var response = await client.GetAsync("/health");

                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var content = await response.Content.ReadAsStringAsync();
                content.Should().Contain("Healthy");
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that GET /ready returns 200 OK when JSON storage is valid and writable.
    /// </summary>
    [Fact]
    public async Task GetReady_ShouldReturn200_WhenJsonStorageValid()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierStorage__ReceiptSigningKey", "some-test-signing-key-long-enough" },
                { "VerifierSecurity__ApiKey", "" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                var response = await client.GetAsync("/ready");

                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var content = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;
                root.GetProperty("status").GetString().Should().Be("Ready");
                root.GetProperty("storage").GetString().Should().Be("json");
                root.GetProperty("signing").GetString().Should().Be("Enabled");
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that GET /ready returns 503 Service Unavailable when Postgres database is unreachable.
    /// </summary>
    [Fact]
    public async Task GetReady_ShouldReturn503_WhenPostgresUnreachable()
    {
        var config = new Dictionary<string, string?>
        {
            { "VerifierEnvironment", "Local" },
            { "VerifierStorage__Provider", "postgres" },
            {
                "VerifierStorage__PostgresConnectionString",
                "Host=127.0.0.1;Port=54321;Database=nonexistent_db_for_test;Username=postgres;Password=badpassword;Timeout=1;"
            },
            { "VerifierSecurity__ApiKey", "" }
        };

        using var client = CreateClientWithEnv(config, out var factory);
        await using (factory)
        {
            var response = await client.GetAsync("/ready");

            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            var content = await response.Content.ReadAsStringAsync();
            content.Should().Contain("Unhealthy");
        }
    }

    /// <summary>
    /// Verifies that API requests are rejected with a 401 and custom message when API key is required but missing.
    /// </summary>
    [Fact]
    public async Task ApiKeyGuard_ShouldRejectMissingApiKey()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKey", "strong-test-api-key-16-chars-long" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                var response = await client.GetAsync("/diagnostics/summary");

                response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
                var content = await response.Content.ReadAsStringAsync();
                content.Should().Be("Verifier API key is required.");
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that API requests are rejected with a 401 and custom message when API key is wrong.
    /// </summary>
    [Fact]
    public async Task ApiKeyGuard_ShouldRejectWrongApiKey()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKey", "strong-test-api-key-16-chars-long" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/summary");
                request.Headers.Add("X-OWS-Verifier-Key", "wrong-api-key-here-12345");

                var response = await client.SendAsync(request);

                response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
                var content = await response.Content.ReadAsStringAsync();
                content.Should().Be("Invalid verifier API key.");
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that API requests succeed when the correct API key is provided.
    /// </summary>
    [Fact]
    public async Task ApiKeyGuard_ShouldAcceptCorrectApiKey()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKey", "strong-test-api-key-16-chars-long" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
                request.Headers.Add("X-OWS-Verifier-Key", "strong-test-api-key-16-chars-long");

                var response = await client.SendAsync(request);

                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var content = await response.Content.ReadAsStringAsync();
                content.Should().Contain("Healthy");
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that the server always returns a request identifier and preserves a supplied one.
    /// </summary>
    [Fact]
    public async Task Requests_ShouldReturnRequestIdHeader()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKey", "" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                using var generatedRequest = new HttpRequestMessage(HttpMethod.Get, "/health");
                var generatedResponse = await client.SendAsync(generatedRequest);
                generatedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                generatedResponse.Headers.TryGetValues("X-Request-Id", out var generatedValues).Should().BeTrue();
                generatedValues.Should().NotBeNull();
                generatedValues!.Single().Should().NotBeNullOrWhiteSpace();

                using var suppliedRequest = new HttpRequestMessage(HttpMethod.Get, "/health");
                suppliedRequest.Headers.Add("X-Request-Id", "req-test-123");
                var suppliedResponse = await client.SendAsync(suppliedRequest);
                suppliedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                suppliedResponse.Headers.GetValues("X-Request-Id").Single().Should().Be("req-test-123");
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that auth failures are audited and raw API keys never land in audit storage.
    /// </summary>
    [Fact]
    public async Task AuditEvents_ShouldRecordAuthFailuresWithoutRawKeys()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        const string operatorKey = "bootstrap-operator-key-1234";
        const string badKey = "definitely-wrong-key-9999";
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKey", operatorKey }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                using var badRequest = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/summary");
                badRequest.Headers.Add("X-OWS-Verifier-Key", badKey);
                var badResponse = await client.SendAsync(badRequest);
                badResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

                using var auditRequest = new HttpRequestMessage(HttpMethod.Get, "/audit/events?eventType=auth.failed");
                auditRequest.Headers.Add("X-OWS-Verifier-Key", operatorKey);
                var auditResponse = await client.SendAsync(auditRequest);
                auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var events = await auditResponse.Content.ReadFromJsonAsync<List<VerifierAuditEvent>>();
                events.Should().NotBeNull();
                events.Should().ContainSingle(e => e.EventType == "auth.failed" && e.Result == "InvalidKey");
                events!.Single().ActorKeyPrefix.Should().Be(badKey[..12]);

                var auditJson = await File.ReadAllTextAsync(Path.Combine(tempDbDir, "audit_events.json"));
                auditJson.Should().NotContain(badKey);
                auditJson.Should().NotContain(operatorKey);
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that reviewer-denied requests are audited and reviewer keys cannot query the global audit feed.
    /// </summary>
    [Fact]
    public async Task AuditEvents_ShouldRecordAccessDeniedAndBlockReviewerAuditQuery()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierStorage__LocalStoragePath", Path.Combine(tempDbDir, "packages") },
                { "VerifierSecurity__ApiKeys__0__Key", "operator-test-api-key-1234" },
                { "VerifierSecurity__ApiKeys__0__Role", "operator" },
                { "VerifierSecurity__ApiKeys__1__Key", "reviewer-test-api-key-1234" },
                { "VerifierSecurity__ApiKeys__1__Role", "reviewer" },
                { "VerifierSecurity__ApiKeys__1__InstitutionId", "inst-1" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                using var deniedRequest = new HttpRequestMessage(HttpMethod.Get, "/audit/events");
                deniedRequest.Headers.Add("X-OWS-Verifier-Key", "reviewer-test-api-key-1234");
                var deniedResponse = await client.SendAsync(deniedRequest);
                deniedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                using var auditRequest = new HttpRequestMessage(HttpMethod.Get, "/audit/events?eventType=access.denied");
                auditRequest.Headers.Add("X-OWS-Verifier-Key", "operator-test-api-key-1234");
                var auditResponse = await client.SendAsync(auditRequest);
                auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var events = await auditResponse.Content.ReadFromJsonAsync<List<VerifierAuditEvent>>();
                events.Should().NotBeNull();
                events.Should().Contain(e => e.EventType == "access.denied" && e.ActorRole == "InstructorReviewer");
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that persisted API keys can be created, listed, used, and revoked without storing raw secrets.
    /// </summary>
    [Fact]
    public async Task PersistentApiKeys_ShouldSupportLifecycleWithoutStoringRawSecret()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKey", "bootstrap-operator-key-1234" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                var createPayload = new
                {
                    role = "Operator"
                };

                using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                createRequest.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                createRequest.Content = JsonContent.Create(createPayload);
                var createResponse = await client.SendAsync(createRequest);
                createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var created = JsonSerializer.Deserialize<JsonElement>(
                    await createResponse.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var rawApiKey = created.GetProperty("apiKey").GetString();
                var keyId = created.GetProperty("metadata").GetProperty("keyId").GetString();
                var keyPrefix = created.GetProperty("metadata").GetProperty("keyPrefix").GetString();
                rawApiKey.Should().NotBeNullOrWhiteSpace();
                keyPrefix.Should().NotBeNullOrWhiteSpace();

                using var healthRequest = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/summary");
                healthRequest.Headers.Add("X-OWS-Verifier-Key", rawApiKey);
                var healthResponse = await client.SendAsync(healthRequest);
                healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/api-keys");
                listRequest.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                var listResponse = await client.SendAsync(listRequest);
                listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var listJson = await listResponse.Content.ReadAsStringAsync();
                listJson.Should().NotContain(rawApiKey);
                var listed = await listResponse.Content.ReadFromJsonAsync<List<VerifierApiKeyMetadata>>();
                listed.Should().NotBeNull();
                var listedKey = listed.Single(key => key.KeyId == keyId);
                listedKey.KeyPrefix.Should().Be(keyPrefix);
                listedKey.LastUsedAtUtc.Should().NotBeNull();

                using var revokeRequest = new HttpRequestMessage(HttpMethod.Post, $"/auth/api-keys/{keyId}/revoke");
                revokeRequest.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                var revokeResponse = await client.SendAsync(revokeRequest);
                revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                using var auditRequest = new HttpRequestMessage(HttpMethod.Get, "/audit/events");
                auditRequest.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                var auditResponse = await client.SendAsync(auditRequest);
                auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var auditEvents = await auditResponse.Content.ReadFromJsonAsync<List<VerifierAuditEvent>>();
                auditEvents.Should().NotBeNull();
                auditEvents!.Any(e =>
                        e.EventType == "api_key.created" &&
                        e.Metadata.TryGetValue("createdKeyPrefix", out var createdPrefix) &&
                        createdPrefix == keyPrefix)
                    .Should().BeTrue();
                auditEvents.Any(e =>
                        e.EventType == "api_key.revoked" &&
                        e.Metadata.TryGetValue("revokedKeyId", out var revokedKeyId) &&
                        revokedKeyId == keyId)
                    .Should().BeTrue();

                using var revokedHealthRequest = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/summary");
                revokedHealthRequest.Headers.Add("X-OWS-Verifier-Key", rawApiKey);
                var revokedHealthResponse = await client.SendAsync(revokedHealthRequest);
                revokedHealthResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

                var apiKeyStorePath = Path.Combine(tempDbDir, "api_keys.json");
                var storedJson = await File.ReadAllTextAsync(apiKeyStorePath);
                storedJson.Should().NotContain(rawApiKey);
                storedJson.Should().Contain(keyPrefix);
                var auditJson = await File.ReadAllTextAsync(Path.Combine(tempDbDir, "audit_events.json"));
                auditJson.Should().NotContain(rawApiKey);
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that expired persisted API keys are rejected.
    /// </summary>
    [Fact]
    public async Task PersistentApiKeys_ShouldRejectExpiredKey()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKey", "bootstrap-operator-key-1234" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                var createPayload = new
                {
                    role = "Operator",
                    expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
                };

                using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                createRequest.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                createRequest.Content = JsonContent.Create(createPayload);
                var createResponse = await client.SendAsync(createRequest);
                createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var created = JsonSerializer.Deserialize<JsonElement>(
                    await createResponse.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var rawApiKey = created.GetProperty("apiKey").GetString();

                using var healthRequest = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/summary");
                healthRequest.Headers.Add("X-OWS-Verifier-Key", rawApiKey);
                var healthResponse = await client.SendAsync(healthRequest);
                healthResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that reviewer keys can read package metadata within their institution scope.
    /// </summary>
    [Fact]
    public async Task ReviewerApiKey_ShouldAllowScopedPackageReads()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKeys__0__Key", "operator-test-api-key-1234" },
                { "VerifierSecurity__ApiKeys__0__Role", "operator" },
                { "VerifierSecurity__ApiKeys__1__Key", "reviewer-test-api-key-1234" },
                { "VerifierSecurity__ApiKeys__1__Role", "reviewer" },
                { "VerifierSecurity__ApiKeys__1__InstitutionId", "inst-1" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                var payload = new VerifierPackageSubmissionRequest
                {
                    InstitutionId = "inst-1",
                    ObjectStorageProvider = "aws",
                    ObjectBucket = "test-bucket",
                    ObjectKey = "reviewable.owspkg",
                    PackageSha256 = new string('a', 64),
                    PackageSizeBytes = 1024
                };

                using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/packages");
                createRequest.Headers.Add("X-OWS-Verifier-Key", "operator-test-api-key-1234");
                createRequest.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8,
                    "application/json");
                var createResponse = await client.SendAsync(createRequest);
                createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var submission = JsonSerializer.Deserialize<VerifierPackageSubmissionResponse>(
                    await createResponse.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                using var readRequest = new HttpRequestMessage(HttpMethod.Get, $"/packages/{submission!.SubmissionId}");
                readRequest.Headers.Add("X-OWS-Verifier-Key", "reviewer-test-api-key-1234");
                var readResponse = await client.SendAsync(readRequest);

                readResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that reviewer keys cannot read package metadata outside their institution scope.
    /// </summary>
    [Fact]
    public async Task ReviewerApiKey_ShouldRejectCrossInstitutionPackageReads()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKeys__0__Key", "operator-test-api-key-1234" },
                { "VerifierSecurity__ApiKeys__0__Role", "operator" },
                { "VerifierSecurity__ApiKeys__1__Key", "reviewer-test-api-key-1234" },
                { "VerifierSecurity__ApiKeys__1__Role", "reviewer" },
                { "VerifierSecurity__ApiKeys__1__InstitutionId", "inst-1" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                var payload = new VerifierPackageSubmissionRequest
                {
                    InstitutionId = "inst-2",
                    ObjectStorageProvider = "aws",
                    ObjectBucket = "test-bucket",
                    ObjectKey = "hidden.owspkg",
                    PackageSha256 = new string('b', 64),
                    PackageSizeBytes = 1024
                };

                using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/packages");
                createRequest.Headers.Add("X-OWS-Verifier-Key", "operator-test-api-key-1234");
                createRequest.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8,
                    "application/json");
                var createResponse = await client.SendAsync(createRequest);
                createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var submission = JsonSerializer.Deserialize<VerifierPackageSubmissionResponse>(
                    await createResponse.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                using var readRequest = new HttpRequestMessage(HttpMethod.Get, $"/packages/{submission!.SubmissionId}");
                readRequest.Headers.Add("X-OWS-Verifier-Key", "reviewer-test-api-key-1234");
                var readResponse = await client.SendAsync(readRequest);

                readResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that reviewer keys can read verification reports for their institution and not for another institution.
    /// </summary>
    [Fact]
    public async Task ReviewerApiKey_ShouldAllowScopedVerificationReport()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-test-pkg-{Guid.NewGuid():N}");
        try
        {
            var packagePath = await CreateTestPackageAsync(projectRoot);
            var fileBytes = await File.ReadAllBytesAsync(packagePath);

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = Convert.ToHexString(sha256.ComputeHash(fileBytes)).ToLowerInvariant();

            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKeys__0__Key", "operator-test-api-key-1234" },
                { "VerifierSecurity__ApiKeys__0__Role", "operator" },
                { "VerifierSecurity__ApiKeys__1__Key", "reviewer-test-api-key-1234" },
                { "VerifierSecurity__ApiKeys__1__Role", "reviewer" },
                { "VerifierSecurity__ApiKeys__1__InstitutionId", "inst-1" },
                { "VerifierSecurity__ApiKeys__2__Key", "reviewer-test-api-key-9999" },
                { "VerifierSecurity__ApiKeys__2__Role", "reviewer" },
                { "VerifierSecurity__ApiKeys__2__InstitutionId", "inst-2" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                var metadataPayload = new VerifierPackageSubmissionRequest
                {
                    InstitutionId = "inst-1",
                    ObjectStorageProvider = "local",
                    ObjectBucket = "packages",
                    ObjectKey = $"{hash}.owspkg",
                    PackageSha256 = hash,
                    PackageSizeBytes = fileBytes.Length
                };

                using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/packages");
                createRequest.Headers.Add("X-OWS-Verifier-Key", "operator-test-api-key-1234");
                createRequest.Content = JsonContent.Create(metadataPayload);
                var createResponse = await client.SendAsync(createRequest);
                createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var submission = await createResponse.Content.ReadFromJsonAsync<VerifierPackageSubmissionResponse>();

                using var uploadContent = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                uploadContent.Add(fileContent, "file", "report.owspkg");
                using var uploadRequest =
                    new HttpRequestMessage(HttpMethod.Put, $"/packages/{submission!.SubmissionId}");
                uploadRequest.Headers.Add("X-OWS-Verifier-Key", "operator-test-api-key-1234");
                uploadRequest.Content = uploadContent;
                var uploadResponse = await client.SendAsync(uploadRequest);
                uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                await WaitForPackageStatusAsync(client, submission.SubmissionId, "operator-test-api-key-1234", "Completed");

                using var allowedRequest =
                    new HttpRequestMessage(HttpMethod.Get, $"/packages/{submission.SubmissionId}/verification");
                allowedRequest.Headers.Add("X-OWS-Verifier-Key", "reviewer-test-api-key-1234");
                var allowedResponse = await client.SendAsync(allowedRequest);
                allowedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                using var deniedRequest =
                    new HttpRequestMessage(HttpMethod.Get, $"/packages/{submission.SubmissionId}/verification");
                deniedRequest.Headers.Add("X-OWS-Verifier-Key", "reviewer-test-api-key-9999");
                var deniedResponse = await client.SendAsync(deniedRequest);
                deniedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }

            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that reviewer keys remain read-only for education metadata and API key management.
    /// </summary>
    [Fact]
    public async Task ReviewerApiKey_ShouldRejectEducationMutationAndApiKeyManagement()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKeys__0__Key", "reviewer-test-api-key-1234" },
                { "VerifierSecurity__ApiKeys__0__Role", "reviewer" },
                { "VerifierSecurity__ApiKeys__0__InstitutionId", "inst-1" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                var institution = new Institution(new InstitutionId("inst-1"), "Institution One", "inst-one",
                    DateTimeOffset.UtcNow);
                using var institutionRequest = new HttpRequestMessage(HttpMethod.Post, "/education/institutions");
                institutionRequest.Headers.Add("X-OWS-Verifier-Key", "reviewer-test-api-key-1234");
                institutionRequest.Content = JsonContent.Create(institution);
                var institutionResponse = await client.SendAsync(institutionRequest);
                institutionResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                using var keyRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                keyRequest.Headers.Add("X-OWS-Verifier-Key", "reviewer-test-api-key-1234");
                keyRequest.Content = JsonContent.Create(new { role = "Operator" });
                var keyResponse = await client.SendAsync(keyRequest);
                keyResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that reviewer keys can read education data for their institution but not another institution.
    /// </summary>
    [Fact]
    public async Task ReviewerApiKey_ShouldRespectEducationInstitutionScope()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKeys__0__Key", "operator-test-api-key-1234" },
                { "VerifierSecurity__ApiKeys__0__Role", "operator" },
                { "VerifierSecurity__ApiKeys__1__Key", "reviewer-test-api-key-1234" },
                { "VerifierSecurity__ApiKeys__1__Role", "reviewer" },
                { "VerifierSecurity__ApiKeys__1__InstitutionId", "inst-1" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                foreach (var institution in new[]
                         {
                             new Institution(new InstitutionId("inst-1"), "Institution One", "inst-one",
                                 DateTimeOffset.UtcNow),
                             new Institution(new InstitutionId("inst-2"), "Institution Two", "inst-two",
                                 DateTimeOffset.UtcNow)
                         })
                {
                    using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/education/institutions");
                    createRequest.Headers.Add("X-OWS-Verifier-Key", "operator-test-api-key-1234");
                    createRequest.Content = JsonContent.Create(institution);
                    var createResponse = await client.SendAsync(createRequest);
                    createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                }

                using var allowedRequest = new HttpRequestMessage(HttpMethod.Get, "/education/institutions/inst-1");
                allowedRequest.Headers.Add("X-OWS-Verifier-Key", "reviewer-test-api-key-1234");
                var allowedResponse = await client.SendAsync(allowedRequest);
                allowedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                using var deniedRequest = new HttpRequestMessage(HttpMethod.Get, "/education/institutions/inst-2");
                deniedRequest.Headers.Add("X-OWS-Verifier-Key", "reviewer-test-api-key-1234");
                var deniedResponse = await client.SendAsync(deniedRequest);
                deniedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that the verifier server fails to start in Production mode when weak/default keys are configured.
    /// </summary>
    [Fact]
    public void Startup_ShouldThrowException_InProductionModeWithInsecureConfig()
    {
        var config = new Dictionary<string, string?>
        {
            { "VerifierEnvironment", "Production" },
            { "VerifierStorage__Provider", "json" },
            { "VerifierStorage__ReceiptSigningKey", "dev-key" },
            { "VerifierSecurity__ApiKey", "weak" }
        };

        var act = () =>
        {
            using var client = CreateClientWithEnv(config, out var factory);
            using (factory)
            {
                _ = factory.Server;
            }
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Fatal configuration errors detected in Production mode. See console output.");
    }

    /// <summary>
    /// Verifies that reviewer API keys must declare an institution scope.
    /// </summary>
    [Fact]
    public void Startup_ShouldThrowException_WhenReviewerKeyHasNoInstitutionScope()
    {
        var config = new Dictionary<string, string?>
        {
            { "VerifierEnvironment", "Local" },
            { "VerifierStorage__Provider", "json" },
            { "VerifierSecurity__ApiKeys__0__Key", "reviewer-test-api-key-1234" },
            { "VerifierSecurity__ApiKeys__0__Role", "reviewer" }
        };

        var act = () =>
        {
            using var client = CreateClientWithEnv(config, out var factory);
            using (factory)
            {
                _ = factory.Server;
            }
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Fatal configuration errors detected in Production mode. See console output.");
    }

    /// <summary>
    /// Verifies that registering metadata (Option B) works correctly and is idempotent.
    /// </summary>
    [Fact]
    public async Task PostPackageMetadata_ShouldRegisterAndBeIdempotent()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKey", "" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                var payload = new VerifierPackageSubmissionRequest
                {
                    SessionId = "some-session-id",
                    ObjectStorageProvider = "aws",
                    ObjectBucket = "test-bucket",
                    ObjectKey = "test-key.owspkg",
                    PackageSha256 = new string('a', 64),
                    PackageSizeBytes = 1024
                };

                using var request1 = new HttpRequestMessage(HttpMethod.Post, "/packages");
                request1.Headers.Add("Idempotency-Key", "key-123");
                request1.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8,
                    "application/json");

                var response1 = await client.SendAsync(request1);
                response1.StatusCode.Should().Be(HttpStatusCode.OK);

                var content1 = await response1.Content.ReadAsStringAsync();
                var submission1 = JsonSerializer.Deserialize<VerifierPackageSubmissionResponse>(content1,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                submission1.Should().NotBeNull();
                submission1.SubmissionId.Should().NotBeNullOrWhiteSpace();

                // Idempotent retry with same payload
                using var request2 = new HttpRequestMessage(HttpMethod.Post, "/packages");
                request2.Headers.Add("Idempotency-Key", "key-123");
                request2.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8,
                    "application/json");

                var response2 = await client.SendAsync(request2);
                response2.StatusCode.Should().Be(HttpStatusCode.OK);
                var content2 = await response2.Content.ReadAsStringAsync();
                var submission2 = JsonSerializer.Deserialize<VerifierPackageSubmissionResponse>(content2,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                submission2!.SubmissionId.Should().Be(submission1.SubmissionId);

                // Different payload same key -> Conflict
                var payloadDiff = payload with { PackageSizeBytes = 9999 };
                using var request3 = new HttpRequestMessage(HttpMethod.Post, "/packages");
                request3.Headers.Add("Idempotency-Key", "key-123");
                request3.Content = new StringContent(JsonSerializer.Serialize(payloadDiff), System.Text.Encoding.UTF8,
                    "application/json");

                var response3 = await client.SendAsync(request3);
                response3.StatusCode.Should().Be(HttpStatusCode.Conflict);
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that package metadata can be listed by verifier session.
    /// </summary>
    [Fact]
    public async Task GetSessionPackages_ShouldReturnPackagesForSessionNewestFirst()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKey", "" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                var firstPayload = new VerifierPackageSubmissionRequest
                {
                    SessionId = "session-a",
                    ObjectStorageProvider = "aws",
                    ObjectBucket = "test-bucket",
                    ObjectKey = "first.owspkg",
                    PackageSha256 = new string('a', 64),
                    PackageSizeBytes = 100
                };

                var secondPayload = new VerifierPackageSubmissionRequest
                {
                    SessionId = "session-a",
                    ObjectStorageProvider = "aws",
                    ObjectBucket = "test-bucket",
                    ObjectKey = "second.owspkg",
                    PackageSha256 = new string('b', 64),
                    PackageSizeBytes = 200
                };

                var otherSessionPayload = new VerifierPackageSubmissionRequest
                {
                    SessionId = "session-b",
                    ObjectStorageProvider = "aws",
                    ObjectBucket = "test-bucket",
                    ObjectKey = "other.owspkg",
                    PackageSha256 = new string('c', 64),
                    PackageSizeBytes = 300
                };

                var firstResponse = await client.PostAsJsonAsync("/packages", firstPayload);
                firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                await Task.Delay(25);

                var secondResponse = await client.PostAsJsonAsync("/packages", secondPayload);
                secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                var otherResponse = await client.PostAsJsonAsync("/packages", otherSessionPayload);
                otherResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                var listResponse = await client.GetAsync("/sessions/session-a/packages");
                listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var packages = await listResponse.Content.ReadFromJsonAsync<List<VerifierPackageSubmissionResponse>>();
                packages.Should().NotBeNull();
                packages.Should().HaveCount(2);
                packages.Select(package => package.SessionId).Should()
                    .OnlyContain(sessionId => sessionId == "session-a");
                packages[0].ObjectKey.Should().Be("second.owspkg");
                packages[1].ObjectKey.Should().Be("first.owspkg");
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that multipart file upload stores the blob durably, queues verification, and exposes the eventual result.
    /// </summary>
    [Fact]
    public async Task PostPackageMultipart_ShouldUploadAndVerify()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-test-pkg-{Guid.NewGuid():N}");
        try
        {
            var packagePath = await CreateTestPackageAsync(projectRoot);
            var fileBytes = await File.ReadAllBytesAsync(packagePath);

            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierStorage__LocalStoragePath", Path.Combine(tempDbDir, "packages") },
                { "VerifierSecurity__ApiKey", "" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                using var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                content.Add(fileContent, "file", Path.GetFileName(packagePath));
                content.Add(new StringContent("session-abc"), "sessionId");

                using var request = new HttpRequestMessage(HttpMethod.Post, "/packages");
                request.Headers.Add("Idempotency-Key", "key-multipart-upload");
                request.Content = content;

                var response = await client.SendAsync(request);
                response.StatusCode.Should().Be(HttpStatusCode.OK);

                var contentJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(contentJson);
                var root = doc.RootElement;

                root.GetProperty("verificationStatus").GetString().Should().Be("Pending");

                var submissionId = root.GetProperty("submissionId").GetString();
                var packageState = await WaitForPackageStatusAsync(client, submissionId!, null, "Completed");
                packageState.GetProperty("blobAvailable").GetBoolean().Should().BeTrue();
                packageState.GetProperty("verificationStatus").GetString().Should().Be("Completed");
                packageState.GetProperty("trustStatus").GetString().Should().Be("Unverified");

                // Fetch verification report later
                var reportResponse = await client.GetAsync($"/packages/{submissionId}/verification");
                reportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var reportJson = await reportResponse.Content.ReadAsStringAsync();

                var reportResult = JsonSerializer.Deserialize<VerificationResult>(reportJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                reportResult.Should().NotBeNull();
                reportResult.TrustStatus.Should().Be(TrustStatus.Unverified);

                var textReportResponse = await client.GetAsync($"/packages/{submissionId}/report");
                textReportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var textReport = await textReportResponse.Content.ReadAsStringAsync();
                textReport.Should().Contain("OWS Verification Report");

                var auditResponse = await client.GetAsync($"/audit/events?packageId={submissionId}&eventType=package.verified");
                auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var auditEvents = await auditResponse.Content.ReadFromJsonAsync<List<VerifierAuditEvent>>();
                auditEvents.Should().NotBeNull();
                auditEvents.Should().ContainSingle(e =>
                    e.EventType == "package.verified" &&
                    e.PackageId == submissionId &&
                    e.Result == TrustStatus.Unverified.ToString());
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
            if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that metadata registration followed by PUT file upload queues worker verification correctly.
    /// </summary>
    [Fact]
    public async Task PutPackageFile_ShouldUploadAndVerify()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-test-pkg-{Guid.NewGuid():N}");
        try
        {
            var packagePath = await CreateTestPackageAsync(projectRoot);
            var fileBytes = await File.ReadAllBytesAsync(packagePath);

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = Convert.ToHexString(sha256.ComputeHash(fileBytes)).ToLowerInvariant();

            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierStorage__LocalStoragePath", Path.Combine(tempDbDir, "packages") },
                { "VerifierSecurity__ApiKey", "" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                // Register metadata first
                var metadataPayload = new VerifierPackageSubmissionRequest
                {
                    SessionId = "session-123",
                    ObjectStorageProvider = "local",
                    ObjectBucket = "packages",
                    ObjectKey = $"{hash}.owspkg",
                    PackageSha256 = hash,
                    PackageSizeBytes = fileBytes.Length
                };

                var regResponse = await client.PostAsJsonAsync("/packages", metadataPayload);
                regResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var regResult = await regResponse.Content.ReadFromJsonAsync<VerifierPackageSubmissionResponse>();
                regResult.Should().NotBeNull();
                var submissionId = regResult.SubmissionId;

                // Attempt verify without file bytes -> should fail
                var verifyFailResponse = await client.PostAsync($"/packages/{submissionId}/verify", null);
                verifyFailResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

                // Upload bytes via PUT
                using var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                content.Add(fileContent, "file", "test.owspkg");

                var putResponse = await client.PutAsync($"/packages/{submissionId}", content);
                putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var putJson = JsonDocument.Parse(await putResponse.Content.ReadAsStringAsync()).RootElement;
                putJson.GetProperty("verificationStatus").GetString().Should().Be("Pending");

                var completedState = await WaitForPackageStatusAsync(client, submissionId!, null, "Completed");
                completedState.GetProperty("trustStatus").GetString().Should().Be("Unverified");

                // Verify triggering POST /packages/{id}/verify now requeues safely
                var verifyResponse = await client.PostAsync($"/packages/{submissionId}/verify", null);
                verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var verifyRoot = JsonDocument.Parse(await verifyResponse.Content.ReadAsStringAsync()).RootElement;
                verifyRoot.GetProperty("verificationStatus").GetString().Should().Be("Pending");
                var reverifiedState = await WaitForPackageStatusAsync(client, submissionId!, null, "Completed");
                reverifiedState.GetProperty("trustStatus").GetString().Should().Be("Unverified");
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
            if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that API-only mode accepts uploads but leaves verification queued.
    /// </summary>
    [Fact]
    public async Task PackageVerificationWorker_DisabledMode_ShouldLeaveJobsPending()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-test-pkg-{Guid.NewGuid():N}");
        const string operatorKey = "bootstrap-operator-key-1234";

        try
        {
            var packagePath = await CreateTestPackageAsync(projectRoot);
            var fileBytes = await File.ReadAllBytesAsync(packagePath);

            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierStorage__LocalStoragePath", Path.Combine(tempDbDir, "packages") },
                { "VerifierSecurity__ApiKey", operatorKey },
                { "PackageVerificationWorker__Enabled", "false" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                using var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                content.Add(fileContent, "file", "test.owspkg");

                using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/packages/upload")
                {
                    Content = content
                };
                uploadRequest.Headers.Add("X-OWS-Verifier-Key", operatorKey);
                var response = await client.SendAsync(uploadRequest);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                using var responseDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var submissionId = responseDoc.RootElement.GetProperty("submissionId").GetString();
                submissionId.Should().NotBeNullOrWhiteSpace();

                await Task.Delay(750);

                using var packageRequest = new HttpRequestMessage(HttpMethod.Get, $"/packages/{submissionId}");
                packageRequest.Headers.Add("X-OWS-Verifier-Key", operatorKey);
                var packageResponse = await client.SendAsync(packageRequest);
                packageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var packageState = JsonDocument.Parse(await packageResponse.Content.ReadAsStringAsync()).RootElement;
                packageState.GetProperty("verificationStatus").GetString().Should().Be("Pending");

                using var diagnosticsRequest = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/summary");
                diagnosticsRequest.Headers.Add("X-OWS-Verifier-Key", operatorKey);
                var diagnosticsResponse = await client.SendAsync(diagnosticsRequest);
                diagnosticsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var diagnostics = JsonDocument.Parse(await diagnosticsResponse.Content.ReadAsStringAsync()).RootElement;
                diagnostics.GetProperty("workerEnabled").GetBoolean().Should().BeFalse();
                diagnostics.GetProperty("instanceMode").GetString().Should().Be("api-only");
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
            if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that package upload rejects non-package payloads before job creation.
    /// </summary>
    [Fact]
    public async Task PostPackageUpload_ShouldRejectInvalidPackageShape()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierStorage__LocalStoragePath", Path.Combine(tempDbDir, "packages") },
                { "VerifierSecurity__ApiKey", "" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                using var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent("not-a-zip"u8.ToArray());
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                content.Add(fileContent, "file", "broken.owspkg");

                var response = await client.PostAsync("/packages/upload", content);
                response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                var body = await response.Content.ReadAsStringAsync();
                body.Should().Contain(".owspkg archive");
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a missing blob causes the worker to fail the package status clearly.
    /// </summary>
    [Fact]
    public async Task PackageVerificationWorker_ShouldFailClearlyWhenBlobIsMissing()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-cli-test-pkg-{Guid.NewGuid():N}");
        try
        {
            var packagePath = await CreateTestPackageAsync(projectRoot);
            var fileBytes = await File.ReadAllBytesAsync(packagePath);
            var packageStoragePath = Path.Combine(tempDbDir, "packages");

            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierStorage__LocalStoragePath", packageStoragePath },
                { "VerifierSecurity__ApiKey", "" },
                { "VerifierStorage__PackageWorkerPollIntervalMilliseconds", "1000" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hash = Convert.ToHexString(sha256.ComputeHash(fileBytes)).ToLowerInvariant();
                var metadataPayload = new VerifierPackageSubmissionRequest
                {
                    SessionId = "session-missing-blob",
                    ObjectStorageProvider = "local",
                    ObjectBucket = "packages",
                    ObjectKey = $"{hash}.owspkg",
                    PackageSha256 = hash,
                    PackageSizeBytes = fileBytes.Length
                };

                var regResponse = await client.PostAsJsonAsync("/packages", metadataPayload);
                regResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var regResult = await regResponse.Content.ReadFromJsonAsync<VerifierPackageSubmissionResponse>();
                regResult.Should().NotBeNull();

                using var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                content.Add(fileContent, "file", "test.owspkg");
                var putResponse = await client.PutAsync($"/packages/{regResult!.SubmissionId}", content);
                putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                File.Delete(Path.Combine(packageStoragePath, $"{hash}.owspkg"));

                var failedState = await WaitForPackageStatusAsync(client, regResult.SubmissionId, null, "Failed");
                failedState.GetProperty("lastVerificationError").GetString().Should().Contain("missing");
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
            if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that diagnostics remain secret-safe while exposing lightweight operator summary data.
    /// </summary>
    [Fact]
    public async Task DiagnosticsSummary_ShouldBeSecretSafe()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        const string operatorKey = "bootstrap-operator-key-1234";
        const string signingKey = "very-strong-signing-key-1234";
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierStorage__ReceiptSigningKey", signingKey },
                { "VerifierSecurity__ApiKey", operatorKey }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                using var startRequest = new HttpRequestMessage(HttpMethod.Post, "/sessions");
                startRequest.Headers.Add("X-OWS-Verifier-Key", operatorKey);
                var startResponse = await client.SendAsync(startRequest);
                startResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                using var diagnosticsRequest = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/summary");
                diagnosticsRequest.Headers.Add("X-OWS-Verifier-Key", operatorKey);
                var diagnosticsResponse = await client.SendAsync(diagnosticsRequest);
                diagnosticsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var diagnosticsJson = await diagnosticsResponse.Content.ReadAsStringAsync();
                diagnosticsJson.Should().Contain("storageProvider");
                diagnosticsJson.Should().Contain("metrics");
                diagnosticsJson.Should().NotContain(operatorKey);
                diagnosticsJson.Should().NotContain(signingKey);
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that readiness and diagnostics expose worker/storage deployment mode without leaking local paths.
    /// </summary>
    [Fact]
    public async Task DeploymentDiagnostics_ShouldExposeWorkerModeSafely()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        const string operatorKey = "bootstrap-operator-key-1234";
        const string signingKey = "very-strong-signing-key-1234";

        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierStorage__ReceiptSigningKey", signingKey },
                { "VerifierSecurity__ApiKey", operatorKey },
                { "PackageVerificationWorker__Enabled", "false" },
                { "VerifierStorage__ApplyMigrationsOnStartup", "false" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                var readyResponse = await client.GetAsync("/ready");
                readyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var readyJson = JsonDocument.Parse(await readyResponse.Content.ReadAsStringAsync()).RootElement;
                readyJson.GetProperty("instanceMode").GetString().Should().Be("api-only");
                readyJson.GetProperty("workerEnabled").GetBoolean().Should().BeFalse();
                readyJson.GetProperty("storageProvider").GetString().Should().Be("json");
                readyJson.GetRawText().Should().NotContain(tempDbDir);

                using var diagnosticsRequest = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/summary");
                diagnosticsRequest.Headers.Add("X-OWS-Verifier-Key", operatorKey);
                var diagnosticsResponse = await client.SendAsync(diagnosticsRequest);
                diagnosticsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var diagnosticsText = await diagnosticsResponse.Content.ReadAsStringAsync();
                diagnosticsText.Should().NotContain(tempDbDir);

                var diagnosticsJson = JsonDocument.Parse(diagnosticsText).RootElement;
                diagnosticsJson.GetProperty("instanceMode").GetString().Should().Be("api-only");
                diagnosticsJson.GetProperty("workerEnabled").GetBoolean().Should().BeFalse();
                diagnosticsJson.GetProperty("packageStorageProvider").GetString().Should().Be("local-file");
                diagnosticsJson.GetProperty("applyMigrationsOnStartup").GetBoolean().Should().BeFalse();
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that size limits and file extensions are checked and rejected if invalid.
    /// </summary>
    [Fact]
    public async Task PostPackage_ShouldRejectInvalidExtensionsAndSizeLimits()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierStorage__MaxPackageSizeBytes", "10" }, // 10 bytes limit
                { "VerifierSecurity__ApiKey", "" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                // Reject invalid extension
                using (var content = new MultipartFormDataContent())
                {
                    var fileContent = new ByteArrayContent(new byte[5]);
                    fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                    content.Add(fileContent, "file", "invalid.txt");

                    var response = await client.PostAsync("/packages", content);
                    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                    var body = await response.Content.ReadAsStringAsync();
                    body.Should().Contain("Only .owspkg files are accepted.");
                }

                // Reject size limit
                using (var content = new MultipartFormDataContent())
                {
                    var fileContent = new ByteArrayContent(new byte[100]);
                    fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                    content.Add(fileContent, "file", "valid.owspkg");

                    var response = await client.PostAsync("/packages", content);
                    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                    var body = await response.Content.ReadAsStringAsync();
                    body.Should().Contain("Uploaded package exceeds maximum size limit.");
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Verifies that POST /sessions/{id}/heartbeat registers heartbeats and manages leases.
    /// </summary>
    [Fact]
    public async Task PostHeartbeat_ShouldRecordSessionHeartbeat()
    {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try
        {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKey", "" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory)
            {
                // Start a session
                var startResponse = await client.PostAsync("/sessions", null);
                startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var startJson = await startResponse.Content.ReadAsStringAsync();
                var startSession = JsonSerializer.Deserialize<StartSessionResponse>(startJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                startSession.Should().NotBeNull();
                var sessionId = startSession.SessionId;

                // Send heartbeat
                var request = new SessionHeartbeatRequest
                {
                    LastKnownEventHash = "last-hash",
                    ClientTimestamp = DateTimeOffset.UtcNow,
                    ClientStatusSummary = "Active"
                };

                var response = await client.PostAsJsonAsync($"/sessions/{sessionId}/heartbeat", request);
                response.StatusCode.Should().Be(HttpStatusCode.OK);

                var json = await response.Content.ReadAsStringAsync();
                var heartbeatResponse = JsonSerializer.Deserialize<SessionHeartbeatResponse>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                heartbeatResponse.Should().NotBeNull();
                heartbeatResponse.SessionHead.SessionId.Should().Be(sessionId);
                heartbeatResponse.SessionTrustState.Should().Be("Active");
                heartbeatResponse.LeaseExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

                // Non-existent session returns 404
                var badResponse = await client.PostAsJsonAsync("/sessions/nonexistent/heartbeat", request);
                badResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir))
            {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }
}
