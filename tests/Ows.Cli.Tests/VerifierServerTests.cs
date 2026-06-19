using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Ows.Cli.Tests;

/// <summary>
/// Integration tests for the OWS Verifier Server endpoints, API key guard, and startup validations.
/// </summary>
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
                // Trigger host building
                _ = factory.Server;
            }
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Fatal configuration errors detected in Production mode. See console output.");
    }
}
