using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Ows.Core.Notarization;
using Ows.Core.Verification;
using Xunit;

namespace Ows.Cli.Tests;

/// <summary>
/// Integration tests for the OWS Verifier Server endpoints, API key guard, and startup validations.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class VerifierServerTests
{
    private static readonly object EnvLock = new();

    /// <summary>
    /// Helper to create a test client and factory with environment variables configured before host building.
    /// </summary>
    private static HttpClient CreateClientWithEnv(Dictionary<string, string?> envVars, out WebApplicationFactory<global::Program> factory)
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
        File.WriteAllText(Path.Combine(projectRoot, "draft.txt"), "draft content");
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
            using (factory)
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
            using (factory)
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
            { "VerifierStorage__PostgresConnectionString", "Host=127.0.0.1;Port=54321;Database=nonexistent_db_for_test;Username=postgres;Password=badpassword;Timeout=1;" },
            { "VerifierSecurity__ApiKey", "" }
        };

        using var client = CreateClientWithEnv(config, out var factory);
        using (factory)
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
            using (factory)
            {
                var response = await client.GetAsync("/health");

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
            using (factory)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
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
            using (factory)
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
            using (factory)
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
                request1.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                var response1 = await client.SendAsync(request1);
                response1.StatusCode.Should().Be(HttpStatusCode.OK);

                var content1 = await response1.Content.ReadAsStringAsync();
                var submission1 = JsonSerializer.Deserialize<VerifierPackageSubmissionResponse>(content1, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                submission1.Should().NotBeNull();
                submission1!.SubmissionId.Should().NotBeNullOrWhiteSpace();

                // Idempotent retry with same payload
                using var request2 = new HttpRequestMessage(HttpMethod.Post, "/packages");
                request2.Headers.Add("Idempotency-Key", "key-123");
                request2.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                var response2 = await client.SendAsync(request2);
                response2.StatusCode.Should().Be(HttpStatusCode.OK);
                var content2 = await response2.Content.ReadAsStringAsync();
                var submission2 = JsonSerializer.Deserialize<VerifierPackageSubmissionResponse>(content2, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                submission2!.SubmissionId.Should().Be(submission1.SubmissionId);

                // Different payload same key -> Conflict
                var payloadDiff = payload with { PackageSizeBytes = 9999 };
                using var request3 = new HttpRequestMessage(HttpMethod.Post, "/packages");
                request3.Headers.Add("Idempotency-Key", "key-123");
                request3.Content = new StringContent(JsonSerializer.Serialize(payloadDiff), System.Text.Encoding.UTF8, "application/json");

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
            using (factory)
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
                packages!.Should().HaveCount(2);
                packages.Select(package => package.SessionId).Should().OnlyContain(sessionId => sessionId == "session-a");
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
    /// Verifies that multipart file upload (Option A) computes hash, verifies it, and exposes results.
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
                { "VerifierSecurity__ApiKey", "" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            using (factory)
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

                root.GetProperty("verificationStatus").GetString().Should().Be("Completed");
                root.GetProperty("trustStatus").GetString().Should().Be("Unverified");

                var submissionId = root.GetProperty("submissionId").GetString();

                // Fetch verification report later
                var reportResponse = await client.GetAsync($"/packages/{submissionId}/verification");
                reportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var reportJson = await reportResponse.Content.ReadAsStringAsync();

                var reportResult = JsonSerializer.Deserialize<VerificationResult>(reportJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                reportResult.Should().NotBeNull();
                reportResult!.TrustStatus.Should().Be(TrustStatus.Unverified);
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
            if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that metadata registration followed by PUT file upload works correctly.
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
                { "VerifierSecurity__ApiKey", "" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            using (factory)
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
                var submissionId = regResult!.SubmissionId;

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

                // Verify triggering POST /packages/{id}/verify now works
                var verifyResponse = await client.PostAsync($"/packages/{submissionId}/verify", null);
                verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                var verifyJson = await verifyResponse.Content.ReadAsStringAsync();
                var verifyResult = JsonSerializer.Deserialize<VerificationResult>(verifyJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                verifyResult.Should().NotBeNull();
                verifyResult!.TrustStatus.Should().Be(TrustStatus.Unverified);
            }
        }
        finally
        {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
            if (Directory.Exists(projectRoot)) Directory.Delete(projectRoot, recursive: true);
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
            using (factory)
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
            using (factory)
            {
                // Start a session
                var startResponse = await client.PostAsync("/sessions", null);
                startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var startJson = await startResponse.Content.ReadAsStringAsync();
                var startSession = JsonSerializer.Deserialize<StartSessionResponse>(startJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                startSession.Should().NotBeNull();
                var sessionId = startSession!.SessionId;

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
                var heartbeatResponse = JsonSerializer.Deserialize<SessionHeartbeatResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                heartbeatResponse.Should().NotBeNull();
                heartbeatResponse!.SessionHead.SessionId.Should().Be(sessionId);
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
