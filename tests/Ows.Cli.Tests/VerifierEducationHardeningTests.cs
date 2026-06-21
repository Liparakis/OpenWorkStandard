using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Ows.Core.Education;
using Ows.Verifier.Server;

namespace Ows.Cli.Tests;

[Collection(CliCommandCollection.Name)]
public sealed class VerifierEducationHardeningTests {
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
                foreach (var key in envVars.Keys) {
                    Environment.SetEnvironmentVariable(key, null);
                }
            }
        }
    }

    [Fact]
    public async Task EducationWrite_ShouldRateLimit_WhenConfigured() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-education-write-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");

        try {
            var config = new Dictionary<string, string?> {
                ["VerifierEnvironment"] = "Local",
                ["VerifierStorage__Provider"] = "json",
                ["VerifierStorage__JsonStorePath"] = tempDbPath,
                ["VerifierSecurity__ApiKey"] = "bootstrap-operator-key-1234",
                ["VerifierRateLimiting__EducationWritePermitLimit"] = "1"
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory) {
                var firstResponse = await PostInstitutionAsync(client, "inst-1");
                firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                var secondResponse = await PostInstitutionAsync(client, "inst-2");
                secondResponse.StatusCode.Should().Be((HttpStatusCode)429);
                var body = await secondResponse.Content.ReadAsStringAsync();
                body.Should().Contain("rate_limit_exceeded");
            }
        } finally {
            if (Directory.Exists(tempDbDir)) {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EducationRead_ShouldRateLimit_WhenConfigured() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-education-read-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");

        try {
            var config = new Dictionary<string, string?> {
                ["VerifierEnvironment"] = "Local",
                ["VerifierStorage__Provider"] = "json",
                ["VerifierStorage__JsonStorePath"] = tempDbPath,
                ["VerifierSecurity__ApiKey"] = "bootstrap-operator-key-1234",
                ["VerifierRateLimiting__EducationReadPermitLimit"] = "1"
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory) {
                (await PostInstitutionAsync(client, "inst-1")).StatusCode.Should().Be(HttpStatusCode.OK);

                var firstResponse = await GetWithOperatorKeyAsync(client, "/education/institutions/inst-1");
                firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                var secondResponse = await GetWithOperatorKeyAsync(client, "/education/institutions/inst-1");
                secondResponse.StatusCode.Should().Be((HttpStatusCode)429);
                var body = await secondResponse.Content.ReadAsStringAsync();
                body.Should().Contain("rate_limit_exceeded");
            }
        } finally {
            if (Directory.Exists(tempDbDir)) {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EducationWrite_ShouldEmitSafeAuditEvent() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-education-audit-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");

        try {
            var config = new Dictionary<string, string?> {
                ["VerifierEnvironment"] = "Local",
                ["VerifierStorage__Provider"] = "json",
                ["VerifierStorage__JsonStorePath"] = tempDbPath,
                ["VerifierSecurity__ApiKey"] = "bootstrap-operator-key-1234"
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory) {
                (await PostInstitutionAsync(client, "inst-1", "Institution One")).StatusCode.Should().Be(HttpStatusCode.OK);

                var auditResponse = await GetWithOperatorKeyAsync(client,
                    "/audit/events?eventType=education.institution.created");
                auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var events = await auditResponse.Content.ReadFromJsonAsync<List<VerifierAuditEvent>>();
                events.Should().NotBeNull();
                events.Should().ContainSingle();

                var auditEvent = events!.Single();
                auditEvent.Result.Should().Be("Created");
                auditEvent.InstitutionId.Should().Be("inst-1");
                auditEvent.Metadata.Should().ContainKey("resourceId");
                auditEvent.Metadata["resourceId"].Should().Be("inst-1");
                auditEvent.Metadata.Should().ContainKey("operation");
                auditEvent.Metadata["operation"].Should().Be("create");
                auditEvent.Metadata.Should().NotContainKey("name");
                auditEvent.Metadata.Should().NotContainKey("payload");

                var auditJson = await File.ReadAllTextAsync(Path.Combine(tempDbDir, "audit_events.json"));
                auditJson.Should().NotContain("Institution One");
                auditJson.Should().NotContain("bootstrap-operator-key-1234");
            }
        } finally {
            if (Directory.Exists(tempDbDir)) {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EnrollmentRosterRead_ShouldAudit_AndAuditQueryLimitShouldClampTo500() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-education-roster-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");

        try {
            var config = new Dictionary<string, string?> {
                ["VerifierEnvironment"] = "Local",
                ["VerifierStorage__Provider"] = "json",
                ["VerifierStorage__JsonStorePath"] = tempDbPath,
                ["VerifierSecurity__ApiKey"] = "bootstrap-operator-key-1234"
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory) {
                await SeedEducationGraphAsync(client);

                var rosterResponse = await GetWithOperatorKeyAsync(client, "/education/enrollments/student/student-1");
                rosterResponse.StatusCode.Should().Be(HttpStatusCode.OK);

                var auditReadResponse = await GetWithOperatorKeyAsync(client,
                    "/audit/events?eventType=education.enrollments.by_student.read");
                auditReadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var readEvents = await auditReadResponse.Content.ReadFromJsonAsync<List<VerifierAuditEvent>>();
                readEvents.Should().NotBeNull();
                readEvents.Should().ContainSingle();
                readEvents!.Single().Metadata.Should().ContainKey("studentUserId");
                readEvents.Single().Metadata["studentUserId"].Should().Be("student-1");

                var auditStore = factory.Services.GetRequiredService<IVerifierAuditStore>();
                for (var i = 0; i < 550; i++) {
                    await auditStore.AppendAsync(new VerifierAuditEvent {
                        Id = Guid.NewGuid().ToString("N"),
                        CreatedAtUtc = DateTimeOffset.UtcNow.AddSeconds(i),
                        EventType = "clamp.test",
                        Result = "Created"
                    }, CancellationToken.None);
                }

                var clampedResponse = await GetWithOperatorKeyAsync(client, "/audit/events?eventType=clamp.test&limit=9999");
                clampedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var clampedEvents = await clampedResponse.Content.ReadFromJsonAsync<List<VerifierAuditEvent>>();
                clampedEvents.Should().NotBeNull();
                clampedEvents.Should().HaveCount(500);
            }
        } finally {
            if (Directory.Exists(tempDbDir)) {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    private static async Task<HttpResponseMessage> PostInstitutionAsync(HttpClient client, string institutionId,
        string? institutionName = null) {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/education/institutions");
        request.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
        request.Content = JsonContent.Create(new Institution(
            new InstitutionId(institutionId),
            institutionName ?? institutionId,
            institutionId,
            DateTimeOffset.UtcNow));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetWithOperatorKeyAsync(HttpClient client, string path) {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
        return await client.SendAsync(request);
    }

    private static async Task SeedEducationGraphAsync(HttpClient client) {
        (await PostInstitutionAsync(client, "inst-1")).StatusCode.Should().Be(HttpStatusCode.OK);

        await PostAsync(client, "/education/courses", new Course(
            new CourseId("course-1"),
            new InstitutionId("inst-1"),
            "CS101",
            "Intro",
            DateTimeOffset.UtcNow));
        await PostAsync(client, "/education/class-groups", new ClassGroup(
            new ClassGroupId("group-1"),
            new InstitutionId("inst-1"),
            "Group 1",
            DateTimeOffset.UtcNow));
        await PostAsync(client, "/education/course-offerings", new CourseOffering(
            new CourseOfferingId("offering-1"),
            new InstitutionId("inst-1"),
            new CourseId("course-1"),
            new ClassGroupId("group-1"),
            "2026-Spring",
            2026,
            DateTimeOffset.UtcNow));
        await PostAsync(client, "/education/users", new User(
            new UserId("student-1"),
            new InstitutionId("inst-1"),
            "Student One",
            "student-1",
            "student1@example.edu",
            DateTimeOffset.UtcNow));
        await PostAsync(client, "/education/enrollments", new StudentEnrollment(
            new EnrollmentId("enrollment-1"),
            new CourseOfferingId("offering-1"),
            new UserId("student-1"),
            DateTimeOffset.UtcNow));
    }

    private static async Task PostAsync<T>(HttpClient client, string path, T payload) {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
        request.Content = JsonContent.Create(payload);
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
