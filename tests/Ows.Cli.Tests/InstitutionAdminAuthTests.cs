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
/// Integration tests verifying the RBAC rules for InstitutionAdmin role,
/// including delegated key creation, education scoping, session creation scoping,
/// and audit scoping.
/// </summary>
[Collection(CliCommandCollection.Name)]
public sealed class InstitutionAdminAuthTests {
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
    public async Task InstitutionAdmin_LifecycleAndAuthRules() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-inst-admin-{Guid.NewGuid():N}");
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
                // Pre-populate institutions
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

                // 1. Operator creates an InstitutionAdmin key
                var createAdminPayload = new {
                    role = "InstitutionAdmin",
                    institutionId = "inst-1"
                };
                using var createAdminReq = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                createAdminReq.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                createAdminReq.Content = JsonContent.Create(createAdminPayload);
                var createAdminRes = await client.SendAsync(createAdminReq);
                createAdminRes.StatusCode.Should().Be(HttpStatusCode.OK);

                var adminKeyObj = JsonSerializer.Deserialize<JsonElement>(
                    await createAdminRes.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var adminApiKey = adminKeyObj.GetProperty("apiKey").GetString();
                adminApiKey.Should().NotBeNullOrWhiteSpace();

                // 2. InstitutionAdmin cannot create Operator key
                var badPayload1 = new { role = "Operator" };
                using var badReq1 = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                badReq1.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                badReq1.Content = JsonContent.Create(badPayload1);
                var badRes1 = await client.SendAsync(badReq1);
                badRes1.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                // 3. InstitutionAdmin cannot create InstitutionAdmin key for anyone
                var badPayload2 = new { role = "InstitutionAdmin", institutionId = "inst-1" };
                using var badReq2 = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                badReq2.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                badReq2.Content = JsonContent.Create(badPayload2);
                var badRes2 = await client.SendAsync(badReq2);
                badRes2.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                // 4. InstitutionAdmin cannot create InstructorReviewer key for a different institution
                var badPayload3 = new { role = "InstructorReviewer", institutionId = "inst-2" };
                using var badReq3 = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                badReq3.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                badReq3.Content = JsonContent.Create(badPayload3);
                var badRes3 = await client.SendAsync(badReq3);
                badRes3.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                // 5. InstitutionAdmin can create InstructorReviewer key for their own institution
                var goodPayload = new { role = "InstructorReviewer", institutionId = "inst-1" };
                using var goodReq = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                goodReq.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                goodReq.Content = JsonContent.Create(goodPayload);
                var goodRes = await client.SendAsync(goodReq);
                goodRes.StatusCode.Should().Be(HttpStatusCode.OK);

                var reviewerKeyObj = JsonSerializer.Deserialize<JsonElement>(
                    await goodRes.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var reviewerApiKey = reviewerKeyObj.GetProperty("apiKey").GetString();
                reviewerApiKey.Should().NotBeNullOrWhiteSpace();

                // 6. InstitutionAdmin can write education data for their own institution
                var courseGood = new Course(new CourseId("course-1"), new InstitutionId("inst-1"), "CS101", "Intro", DateTimeOffset.UtcNow);
                using var courseGoodReq = new HttpRequestMessage(HttpMethod.Post, "/education/courses");
                courseGoodReq.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                courseGoodReq.Content = JsonContent.Create(courseGood);
                var courseGoodRes = await client.SendAsync(courseGoodReq);
                courseGoodRes.StatusCode.Should().Be(HttpStatusCode.OK);

                // 7. InstitutionAdmin cannot write education data for a different institution
                var courseBad = new Course(new CourseId("course-2"), new InstitutionId("inst-2"), "CS102", "Intro 2", DateTimeOffset.UtcNow);
                using var courseBadReq = new HttpRequestMessage(HttpMethod.Post, "/education/courses");
                courseBadReq.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                courseBadReq.Content = JsonContent.Create(courseBad);
                var courseBadRes = await client.SendAsync(courseBadReq);
                courseBadRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                // 8. InstructorReviewer cannot write education data (rejected at middleware)
                var courseRev = new Course(new CourseId("course-3"), new InstitutionId("inst-1"), "CS103", "Intro 3", DateTimeOffset.UtcNow);
                using var courseRevReq = new HttpRequestMessage(HttpMethod.Post, "/education/courses");
                courseRevReq.Headers.Add("X-OWS-Verifier-Key", reviewerApiKey);
                courseRevReq.Content = JsonContent.Create(courseRev);
                var courseRevRes = await client.SendAsync(courseRevReq);
                courseRevRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                // 9. InstitutionAdmin cannot write to another institution's resource via PUT/POST (tested above, but verify specific resource lookup route if any)
                // Let's create an assessment for inst-1
                var assessment = new Assessment(new AssessmentId("assess-1"), new InstitutionId("inst-1"), new CourseOfferingId("offering-1"), "Exam", null, null, null, DateTimeOffset.UtcNow);
                using var assessReq = new HttpRequestMessage(HttpMethod.Post, "/education/assessments");
                assessReq.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                assessReq.Content = JsonContent.Create(assessment);
                var assessRes = await client.SendAsync(assessReq);
                assessRes.StatusCode.Should().Be(HttpStatusCode.OK);

                // 10. POST /sessions: InstitutionAdmin may create sessions for own institution, but not other
                using var adminSessionReq = new HttpRequestMessage(HttpMethod.Post, "/sessions");
                adminSessionReq.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                adminSessionReq.Content = JsonContent.Create(new { institutionId = "inst-1" });
                var adminSessionRes = await client.SendAsync(adminSessionReq);
                adminSessionRes.StatusCode.Should().Be(HttpStatusCode.OK);

                using var adminSessionBadReq = new HttpRequestMessage(HttpMethod.Post, "/sessions");
                adminSessionBadReq.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                adminSessionBadReq.Content = JsonContent.Create(new { institutionId = "inst-2" });
                var adminSessionBadRes = await client.SendAsync(adminSessionBadReq);
                adminSessionBadRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                // 11. Audit events filter enforced for InstitutionAdmin
                // First write some events under inst-2 to ensure they exist
                using var auditWriteReq = new HttpRequestMessage(HttpMethod.Post, "/sessions");
                auditWriteReq.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                auditWriteReq.Content = JsonContent.Create(new { institutionId = "inst-2" });
                var auditWriteRes = await client.SendAsync(auditWriteReq);
                auditWriteRes.StatusCode.Should().Be(HttpStatusCode.OK);

                // Query audit events as Operator (should see everything)
                using var auditOpReq = new HttpRequestMessage(HttpMethod.Get, "/audit/events");
                auditOpReq.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                var auditOpRes = await client.SendAsync(auditOpReq);
                var opEvents = await auditOpRes.Content.ReadFromJsonAsync<List<VerifierAuditEvent>>();
                opEvents.Should().NotBeNull();
                opEvents.Any(e => e.InstitutionId == "inst-2").Should().BeTrue();

                // Query audit events as InstitutionAdmin of inst-1 (must NOT see inst-2 events, even if unfiltered request is sent)
                using var auditAdminReq = new HttpRequestMessage(HttpMethod.Get, "/audit/events");
                auditAdminReq.Headers.Add("X-OWS-Verifier-Key", adminApiKey);
                var auditAdminRes = await client.SendAsync(auditAdminReq);
                auditAdminRes.StatusCode.Should().Be(HttpStatusCode.OK);
                var adminEvents = await auditAdminRes.Content.ReadFromJsonAsync<List<VerifierAuditEvent>>();
                adminEvents.Should().NotBeNull();
                adminEvents!.Any(e => e.InstitutionId == "inst-2").Should().BeFalse();
            }
        } finally {
            if (Directory.Exists(tempDbDir)) {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }
}
