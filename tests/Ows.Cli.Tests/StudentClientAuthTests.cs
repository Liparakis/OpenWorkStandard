using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Ows.Core.Education;
using Ows.Verifier.Server;
using Xunit;

namespace Ows.Cli.Tests;

/// <summary>
/// Integration tests verifying the RBAC rules for StudentClient role,
/// including session start constraints, heartbeat/checkpoint scope limits,
/// package upload limits, report read limits, and metrics endpoint.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class StudentClientAuthTests {
    private static readonly Lock EnvLock = new();

    private static HttpClient CreateClientWithEnv(Dictionary<string, string?> envVars,
        out WebApplicationFactory<global::Program> factory) {
        lock (EnvLock) {
            foreach (var (k, v) in envVars) {
                Environment.SetEnvironmentVariable(k, v);
            }

            try {
                factory = new WebApplicationFactory<global::Program>();
                return factory.CreateClient();
            } finally {
                foreach (var k in envVars.Keys) {
                    Environment.SetEnvironmentVariable(k, null);
                }
            }
        }
    }

    [Fact]
    public async Task StudentClient_LifecycleAndAuthRules() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-student-client-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierStorage__LocalStoragePath", Path.Combine(tempDbDir, "packages") },
                { "VerifierSecurity__ApiKey", "bootstrap-operator-key-1234" }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory) {
                // Pre-populate institutions & users
                var inst1 = new Institution(new InstitutionId("inst-1"), "Institution 1", "inst-1", DateTimeOffset.UtcNow);
                var inst2 = new Institution(new InstitutionId("inst-2"), "Institution 2", "inst-2", DateTimeOffset.UtcNow);

                using var createInst1Req = new HttpRequestMessage(HttpMethod.Post, "/education/institutions");
                createInst1Req.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                createInst1Req.Content = JsonContent.Create(inst1);
                var createInst1Res = await client.SendAsync(createInst1Req);
                createInst1Res.StatusCode.Should().Be(HttpStatusCode.OK);

                using var createInst2Req = new HttpRequestMessage(HttpMethod.Post, "/education/institutions");
                createInst2Req.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                createInst2Req.Content = JsonContent.Create(inst2);
                var createInst2Res = await client.SendAsync(createInst2Req);
                createInst2Res.StatusCode.Should().Be(HttpStatusCode.OK);

                var student1User = new User(new UserId("student-1"), new InstitutionId("inst-1"), "Student One", "student-1", "student1@inst.edu", DateTimeOffset.UtcNow);
                using var createStudent1Req = new HttpRequestMessage(HttpMethod.Post, "/education/users");
                createStudent1Req.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                createStudent1Req.Content = JsonContent.Create(student1User);
                var createStudent1Res = await client.SendAsync(createStudent1Req);
                createStudent1Res.StatusCode.Should().Be(HttpStatusCode.OK);

                var student2User = new User(new UserId("student-2"), new InstitutionId("inst-1"), "Student Two", "student-2", "student2@inst.edu", DateTimeOffset.UtcNow);
                using var createStudent2Req = new HttpRequestMessage(HttpMethod.Post, "/education/users");
                createStudent2Req.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                createStudent2Req.Content = JsonContent.Create(student2User);
                var createStudent2Res = await client.SendAsync(createStudent2Req);
                createStudent2Res.StatusCode.Should().Be(HttpStatusCode.OK);

                // Operator creates an InstitutionAdmin key for inst-1
                var createAdminPayload = new { role = "InstitutionAdmin", institutionId = "inst-1" };
                using var createAdminReq = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                createAdminReq.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                createAdminReq.Content = JsonContent.Create(createAdminPayload);
                var createAdminRes = await client.SendAsync(createAdminReq);
                createAdminRes.StatusCode.Should().Be(HttpStatusCode.OK);

                var adminKeyObj = JsonSerializer.Deserialize<JsonElement>(
                    await createAdminRes.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var adminApiKey = adminKeyObj.GetProperty("apiKey").GetString();

                // 1. InstitutionAdmin creates a StudentClient key bound to student-1
                var createStudent1Payload = new {
                    role = "StudentClient",
                    institutionId = "inst-1",
                    studentUserId = "student-1"
                };
                using var createStudent1KeyReq = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                createStudent1KeyReq.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                createStudent1KeyReq.Content = JsonContent.Create(createStudent1Payload);
                var createStudent1KeyRes = await client.SendAsync(createStudent1KeyReq);
                createStudent1KeyRes.StatusCode.Should().Be(HttpStatusCode.OK);

                var student1KeyObj = JsonSerializer.Deserialize<JsonElement>(
                    await createStudent1KeyRes.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var student1ApiKey = student1KeyObj.GetProperty("apiKey").GetString();

                // 2. InstitutionAdmin creates an unbound StudentClient key
                var createUnboundPayload = new {
                    role = "StudentClient",
                    institutionId = "inst-1"
                };
                using var createUnboundKeyReq = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                createUnboundKeyReq.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                createUnboundKeyReq.Content = JsonContent.Create(createUnboundPayload);
                var createUnboundKeyRes = await client.SendAsync(createUnboundKeyReq);
                createUnboundKeyRes.StatusCode.Should().Be(HttpStatusCode.OK);

                var unboundKeyObj = JsonSerializer.Deserialize<JsonElement>(
                    await createUnboundKeyRes.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var unboundApiKey = unboundKeyObj.GetProperty("apiKey").GetString();

                // 3. InstitutionAdmin cannot create StudentClient key for inst-2
                var badStudentPayload = new {
                    role = "StudentClient",
                    institutionId = "inst-2"
                };
                using var badStudentKeyReq = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                badStudentKeyReq.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                badStudentKeyReq.Content = JsonContent.Create(badStudentPayload);
                var badStudentKeyRes = await client.SendAsync(badStudentKeyReq);
                badStudentKeyRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                // 4. StudentClient cannot create any keys
                var createDelegatePayload = new { role = "StudentClient", institutionId = "inst-1" };
                using var createDelegateReq = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                createDelegateReq.Headers.Add("X-OWS-Verifier-Key", student1ApiKey);
                createDelegateReq.Content = JsonContent.Create(createDelegatePayload);
                var createDelegateRes = await client.SendAsync(createDelegateReq);
                createDelegateRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                // 5. Bound student-1 key starts a session for student-1 (succeeds)
                using var startSessionReq = new HttpRequestMessage(HttpMethod.Post, "/sessions");
                startSessionReq.Headers.Add("X-OWS-Verifier-Key", student1ApiKey);
                startSessionReq.Content = JsonContent.Create(new {
                    institutionId = "inst-1",
                    studentUserId = "student-1"
                });
                var startSessionRes = await client.SendAsync(startSessionReq);
                startSessionRes.StatusCode.Should().Be(HttpStatusCode.OK);

                var session1Obj = JsonSerializer.Deserialize<JsonElement>(
                    await startSessionRes.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var session1Id = session1Obj.GetProperty("sessionId").GetString();

                // 6. Bound student-1 key starts a session for student-2 (fails with 403)
                using var startSession2Req = new HttpRequestMessage(HttpMethod.Post, "/sessions");
                startSession2Req.Headers.Add("X-OWS-Verifier-Key", student1ApiKey);
                startSession2Req.Content = JsonContent.Create(new {
                    institutionId = "inst-1",
                    studentUserId = "student-2"
                });
                var startSession2Res = await client.SendAsync(startSession2Req);
                startSession2Res.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                // 7. Unbound key starts a session for student-2 (succeeds)
                using var startSessionUnboundReq = new HttpRequestMessage(HttpMethod.Post, "/sessions");
                startSessionUnboundReq.Headers.Add("X-OWS-Verifier-Key", unboundApiKey);
                startSessionUnboundReq.Content = JsonContent.Create(new {
                    institutionId = "inst-1",
                    studentUserId = "student-2"
                });
                var startSessionUnboundRes = await client.SendAsync(startSessionUnboundReq);
                startSessionUnboundRes.StatusCode.Should().Be(HttpStatusCode.OK);

                var session2Obj = JsonSerializer.Deserialize<JsonElement>(
                    await startSessionUnboundRes.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var session2Id = session2Obj.GetProperty("sessionId").GetString();

                // 8. StudentClient starts a session for inst-2 (fails with 403)
                using var startSessionInst2Req = new HttpRequestMessage(HttpMethod.Post, "/sessions");
                startSessionInst2Req.Headers.Add("X-OWS-Verifier-Key", student1ApiKey);
                startSessionInst2Req.Content = JsonContent.Create(new {
                    institutionId = "inst-2"
                });
                var startSessionInst2Res = await client.SendAsync(startSessionInst2Req);
                startSessionInst2Res.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                // 9. Bound student-1 key heartbeats/checkpoints their own session (succeeds)
                using var heartbeatReq = new HttpRequestMessage(HttpMethod.Post, $"/sessions/{session1Id}/heartbeat");
                heartbeatReq.Headers.Add("X-OWS-Verifier-Key", student1ApiKey);
                heartbeatReq.Content = JsonContent.Create(new { lastKnownEventHash = "abc" });
                var heartbeatRes = await client.SendAsync(heartbeatReq);
                heartbeatRes.StatusCode.Should().Be(HttpStatusCode.OK);

                using var checkpointReq = new HttpRequestMessage(HttpMethod.Post, $"/sessions/{session1Id}/checkpoints");
                checkpointReq.Headers.Add("X-OWS-Verifier-Key", student1ApiKey);
                checkpointReq.Content = JsonContent.Create(new {
                    sessionId = session1Id,
                    sequenceNumber = 1,
                    timelineHeadHash = "def"
                });
                var checkpointRes = await client.SendAsync(checkpointReq);
                checkpointRes.StatusCode.Should().Be(HttpStatusCode.OK);

                // 10. Bound student-1 key heartbeats/checkpoints student-2's session (fails with 403)
                using var heartbeatReq2 = new HttpRequestMessage(HttpMethod.Post, $"/sessions/{session2Id}/heartbeat");
                heartbeatReq2.Headers.Add("X-OWS-Verifier-Key", student1ApiKey);
                heartbeatReq2.Content = JsonContent.Create(new { lastKnownEventHash = "abc" });
                var heartbeatRes2 = await client.SendAsync(heartbeatReq2);
                heartbeatRes2.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                // 11. Package submission checks
                // Register a package for session-1 (owned by student-1) using bound key (succeeds)
                var submitPayload = new {
                    sessionId = session1Id,
                    packageSha256 = "1111222233334444555566667777888899990000111122223333444455556666",
                    packageSizeBytes = 100,
                    objectStorageProvider = "local",
                    objectBucket = "packages",
                    objectKey = "some-key.owspkg"
                };
                using var submitReq = new HttpRequestMessage(HttpMethod.Post, "/packages");
                submitReq.Headers.Add("X-OWS-Verifier-Key", student1ApiKey);
                submitReq.Content = JsonContent.Create(submitPayload);
                var submitRes = await client.SendAsync(submitReq);
                submitRes.StatusCode.Should().Be(HttpStatusCode.OK);

                var pkgObj = JsonSerializer.Deserialize<JsonElement>(
                    await submitRes.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var packageId = pkgObj.GetProperty("submissionId").GetString();

                // Register a package for session-2 (student-2) using bound student-1 key (fails with 403)
                var submitPayload2 = new {
                    sessionId = session2Id,
                    packageSha256 = "2222222233334444555566667777888899990000111122223333444455556666",
                    packageSizeBytes = 100,
                    objectStorageProvider = "local",
                    objectBucket = "packages",
                    objectKey = "other-key.owspkg"
                };
                using var submitReq2 = new HttpRequestMessage(HttpMethod.Post, "/packages");
                submitReq2.Headers.Add("X-OWS-Verifier-Key", student1ApiKey);
                submitReq2.Content = JsonContent.Create(submitPayload2);
                var submitRes2 = await client.SendAsync(submitReq2);
                submitRes2.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                // 12. Package status/report read checks
                // Bound key reads its own package (succeeds)
                using var readPkgReq = new HttpRequestMessage(HttpMethod.Get, $"/packages/{packageId}");
                readPkgReq.Headers.Add("X-OWS-Verifier-Key", student1ApiKey);
                var readPkgRes = await client.SendAsync(readPkgReq);
                readPkgRes.StatusCode.Should().Be(HttpStatusCode.OK);

                // Unbound key reads package status (fails with 403, as unbound keys cannot read packages/reports)
                using var readPkgUnboundReq = new HttpRequestMessage(HttpMethod.Get, $"/packages/{packageId}");
                readPkgUnboundReq.Headers.Add("X-OWS-Verifier-Key", unboundApiKey);
                var readPkgUnboundRes = await client.SendAsync(readPkgUnboundReq);
                readPkgUnboundRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                // Bound student-1 key reads verification report for its package (returns 404 or 200, but not 403)
                using var readReportReq = new HttpRequestMessage(HttpMethod.Get, $"/packages/{packageId}/verification");
                readReportReq.Headers.Add("X-OWS-Verifier-Key", student1ApiKey);
                var readReportRes = await client.SendAsync(readReportReq);
                readReportRes.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);

                // 13. Metrics Endpoint validation
                // Scrape metrics anonymously
                using var metricsReq = new HttpRequestMessage(HttpMethod.Get, "/metrics");
                var metricsRes = await client.SendAsync(metricsReq);
                metricsRes.StatusCode.Should().Be(HttpStatusCode.OK);

                var metricsOutput = await metricsRes.Content.ReadAsStringAsync();
                metricsOutput.Should().Contain("# HELP ows_sessions_created_total");
                metricsOutput.Should().Contain("# TYPE ows_sessions_created_total counter");
                metricsOutput.Should().Contain("ows_sessions_created_total");
                metricsOutput.Should().Contain("ows_ready_dependency_status{dependency=\"storage\"}");

                // Ensure it does not leak any bootstrap or persisted API key secrets
                metricsOutput.Should().NotContain("bootstrap-operator-key-1234");
                metricsOutput.Should().NotContain(adminApiKey);
                metricsOutput.Should().NotContain(student1ApiKey);
                metricsOutput.Should().NotContain(unboundApiKey);
            }
        } finally {
            if (Directory.Exists(tempDbDir)) {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }
}
