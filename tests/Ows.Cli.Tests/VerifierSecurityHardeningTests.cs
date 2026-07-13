using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Ows.Core;

namespace Ows.Cli.Tests;

[Collection(CliCommandCollection.Name)]
public sealed class VerifierSecurityHardeningTests {
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

    private static byte[] CreatePackageBytes(params (string Path, string Content)[] extraEntries) {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            WriteEntry(archive, OwsConstants.ManifestFileName, "{}");
            WriteEntry(archive, OwsConstants.TimelineFileName, "[]");
            WriteEntry(archive, OwsConstants.VersionGraphFileName, "{}");

            foreach (var (path, content) in extraEntries) {
                WriteEntry(archive, path, content);
            }
        }

        return stream.ToArray();

        static void WriteEntry(ZipArchive archive, string path, string content) {
            var entry = archive.CreateEntry(path);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }

    [Fact]
    public async Task AuthManagement_ShouldRateLimit_WhenConfigured() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-rate-limit-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");

        try {
            var config = new Dictionary<string, string?> {
                ["VerifierEnvironment"] = "Local",
                ["VerifierStorage__Provider"] = "json",
                ["VerifierStorage__JsonStorePath"] = tempDbPath,
                ["VerifierSecurity__ApiKey"] = "bootstrap-operator-key-1234",
                ["VerifierRateLimiting__AuthPermitLimit"] = "2"
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory) {
                for (var i = 0; i < 2; i++) {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                    request.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                    request.Content = JsonContent.Create(new { role = "Operator" });
                    var response = await client.SendAsync(request);
                    response.StatusCode.Should().Be(HttpStatusCode.OK);
                }

                using var throttledRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
                throttledRequest.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
                throttledRequest.Content = JsonContent.Create(new { role = "Operator" });
                var throttledResponse = await client.SendAsync(throttledRequest);
                throttledResponse.StatusCode.Should().Be((HttpStatusCode) 429);
                var body = await throttledResponse.Content.ReadAsStringAsync();
                body.Should().Contain("rate_limit_exceeded");
            }
        } finally {
            if (Directory.Exists(tempDbDir)) {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ScopedUpload_ShouldRejectBeforeBlobPersistence() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-upload-scope-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        var packageStoragePath = Path.Combine(tempDbDir, "packages");

        try {
            var config = new Dictionary<string, string?> {
                ["VerifierEnvironment"] = "Local",
                ["VerifierStorage__Provider"] = "json",
                ["VerifierStorage__JsonStorePath"] = tempDbPath,
                ["VerifierStorage__LocalStoragePath"] = packageStoragePath,
                ["VerifierSecurity__ApiKey"] = "bootstrap-operator-key-1234"
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory) {
                var studentKey = await CreateStudentKeyAsync(client, "inst-1", "student-1");
                var sessionId = await StartSessionAsync(client, "bootstrap-operator-key-1234", "inst-1", "student-2");

                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(sessionId), "sessionId");
                content.Add(new ByteArrayContent(CreatePackageBytes()), "file", "scoped.owspkg");

                using var request = new HttpRequestMessage(HttpMethod.Post, "/packages/upload") {
                    Content = content
                };
                request.Headers.Add("X-OWS-Verifier-Key", studentKey);

                var response = await client.SendAsync(request);
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

                (Directory.Exists(packageStoragePath)
                        ? Directory.GetFiles(packageStoragePath, "*.owspkg", SearchOption.TopDirectoryOnly)
                        : [])
                    .Should().BeEmpty();
            }
        } finally {
            if (Directory.Exists(tempDbDir)) {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PackageUpload_ShouldRejectUnsafeArchiveEntryPaths() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-unsafe-archive-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        var packageStoragePath = Path.Combine(tempDbDir, "packages");

        try {
            var config = new Dictionary<string, string?> {
                ["VerifierEnvironment"] = "Local",
                ["VerifierStorage__Provider"] = "json",
                ["VerifierStorage__JsonStorePath"] = tempDbPath,
                ["VerifierStorage__LocalStoragePath"] = packageStoragePath,
                ["VerifierSecurity__ApiKey"] = "bootstrap-operator-key-1234"
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory) {
                using var content = new MultipartFormDataContent();
                content.Add(new ByteArrayContent(CreatePackageBytes(("../escape.txt", "bad"))), "file", "unsafe.owspkg");

                using var request = new HttpRequestMessage(HttpMethod.Post, "/packages/upload") {
                    Content = content
                };
                request.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");

                var response = await client.SendAsync(request);
                response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                var body = await response.Content.ReadAsStringAsync();
                body.Should().Contain("entry path");

                (Directory.Exists(packageStoragePath)
                        ? Directory.GetFiles(packageStoragePath, "*.owspkg", SearchOption.TopDirectoryOnly)
                        : [])
                    .Should().BeEmpty();
            }
        } finally {
            if (Directory.Exists(tempDbDir)) {
                Directory.Delete(tempDbDir, recursive: true);
            }
        }
    }

    private static async Task<string> CreateStudentKeyAsync(HttpClient client, string institutionId, string studentUserId) {
        using var adminRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
        adminRequest.Headers.Add("X-OWS-Verifier-Key", "bootstrap-operator-key-1234");
        adminRequest.Content = JsonContent.Create(new { role = "InstitutionAdmin", institutionId });
        var adminResponse = await client.SendAsync(adminRequest);
        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var adminJson = JsonDocument.Parse(await adminResponse.Content.ReadAsStringAsync()).RootElement;
        var adminKey = adminJson.GetProperty("apiKey").GetString();

        using var studentRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/api-keys");
        studentRequest.Headers.Add("X-OWS-Verifier-Key", adminKey);
        studentRequest.Content = JsonContent.Create(new { role = "StudentClient", institutionId, studentUserId });
        var studentResponse = await client.SendAsync(studentRequest);
        studentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var studentJson = JsonDocument.Parse(await studentResponse.Content.ReadAsStringAsync()).RootElement;
        return studentJson.GetProperty("apiKey").GetString()!;
    }

    private static async Task<string> StartSessionAsync(HttpClient client, string apiKey, string institutionId,
        string studentUserId) {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/sessions");
        request.Headers.Add("X-OWS-Verifier-Key", apiKey);
        request.Content = JsonContent.Create(new { institutionId, studentUserId });
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return json.GetProperty("sessionId").GetString()!;
    }
}
