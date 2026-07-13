using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Ows.Verifier.Server;

namespace Ows.Cli.Tests;

[Collection(CliCommandCollection.Name)]
public sealed class OidcAuthTests {
    private const string TestIssuer = "https://issuer.example";
    private const string TestAudience = "ows-verifier";
    private const string SigningKey = "ows-oidc-test-signing-key-1234567890";
    private static readonly Lock EnvLock = new();

    private static HttpClient CreateClientWithEnv(
        Dictionary<string, string?> envVars,
        out WebApplicationFactory<global::Program> factory,
        Action<Microsoft.AspNetCore.Hosting.IWebHostBuilder>? configureBuilder = null) {
        lock (EnvLock) {
            foreach (var (k, v) in envVars) {
                Environment.SetEnvironmentVariable(k, v);
            }

            try {
                WebApplicationFactory<global::Program> baseFactory = new WebApplicationFactory<global::Program>();
                factory = configureBuilder is null ? baseFactory : baseFactory.WithWebHostBuilder(configureBuilder);
                return factory.CreateClient();
            } finally {
                foreach (var key in envVars.Keys) {
                    Environment.SetEnvironmentVariable(key, null);
                }
            }
        }
    }

    private static void ConfigureTestBearerValidation(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) {
        builder.ConfigureServices(services => {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => {
                options.RequireHttpsMetadata = false;
                options.MapInboundClaims = false;
                options.Authority = TestIssuer;
                options.Audience = TestAudience;
                options.Configuration = new OpenIdConnectConfiguration {
                    Issuer = TestIssuer
                };
                options.TokenValidationParameters = new TokenValidationParameters {
                    ValidateIssuer = true,
                    ValidIssuer = TestIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    NameClaimType = "name",
                    RoleClaimType = "role"
                };
            });
        });
    }

    private static string CreateBearerToken(params Claim[] claims) {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var descriptor = new SecurityTokenDescriptor {
            Subject = new ClaimsIdentity(claims, "Bearer"),
            Expires = DateTime.UtcNow.AddMinutes(30),
            Issuer = TestIssuer,
            Audience = TestAudience,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static AuthenticationHeaderValue CreateBearerHeader(params Claim[] claims) =>
        new("Bearer", CreateBearerToken(claims));

    private static Dictionary<string, string?> CreateOidcConfig(string tempDbPath, string? apiKey = null, string? clientSecret = null) =>
        new()
        {
            { "VerifierEnvironment", "Local" },
            { "VerifierStorage__Provider", "json" },
            { "VerifierStorage__JsonStorePath", tempDbPath },
            { "VerifierStorage__LocalStoragePath", Path.Combine(Path.GetDirectoryName(tempDbPath)!, "packages") },
            { "VerifierAuth__Oidc__Enabled", "true" },
            { "VerifierAuth__Oidc__Authority", TestIssuer },
            { "VerifierAuth__Oidc__Audience", TestAudience },
            { "VerifierAuth__Oidc__RequireHttpsMetadata", "false" },
            { "VerifierAuth__Oidc__RoleClaim", "role" },
            { "VerifierAuth__Oidc__InstitutionClaim", "institution" },
            { "VerifierAuth__Oidc__UserIdClaim", "sub" },
            { "VerifierAuth__Oidc__EmailClaim", "email" },
            { "VerifierAuth__Oidc__DisplayNameClaim", "name" },
            { "VerifierAuth__Oidc__ClientSecret", clientSecret },
            { "VerifierSecurity__ApiKey", apiKey ?? string.Empty }
        };

    [Fact]
    public async Task Ready_ShouldExposeOidcDisabledByDefault() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKey", string.Empty }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory) {
                var response = await client.GetAsync("/ready");
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                root.GetProperty("dependencies").GetProperty("oidc").GetProperty("enabled").GetBoolean().Should().BeFalse();
            }
        } finally {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
        }
    }

    [Fact]
    public async Task ApiKeyOnly_ShouldStillWork_WhenOidcDisabled() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        const string operatorKey = "bootstrap-operator-key-1234";
        try {
            var config = new Dictionary<string, string?>
            {
                { "VerifierEnvironment", "Local" },
                { "VerifierStorage__Provider", "json" },
                { "VerifierStorage__JsonStorePath", tempDbPath },
                { "VerifierSecurity__ApiKey", operatorKey }
            };

            using var client = CreateClientWithEnv(config, out var factory);
            await using (factory) {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/summary");
                request.Headers.Add("X-OWS-Verifier-Key", operatorKey);
                var response = await client.SendAsync(request);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                root.GetProperty("oidc").GetProperty("enabled").GetBoolean().Should().BeFalse();
            }
        } finally {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
        }
    }

    [Fact]
    public async Task BearerOnly_ShouldSucceed_ForOperatorClaims_WhenOidcEnabled() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try {
            using var client = CreateClientWithEnv(
                CreateOidcConfig(tempDbPath),
                out var factory,
                ConfigureTestBearerValidation);
            await using (factory) {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/summary");
                request.Headers.Authorization = CreateBearerHeader(
                    new Claim("role", "Operator"),
                    new Claim("sub", "user-1"),
                    new Claim("email", "operator@example.edu"),
                    new Claim("name", "Operator One"));
                var response = await client.SendAsync(request);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
            }
        } finally {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
        }
    }

    [Fact]
    public async Task BearerOnly_ShouldMapInstitutionAdmin_WithInstitutionScope() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        const string operatorKey = "bootstrap-operator-key-1234";
        try {
            using var client = CreateClientWithEnv(
                CreateOidcConfig(tempDbPath, operatorKey),
                out var factory,
                ConfigureTestBearerValidation);
            await using (factory) {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/sessions") {
                    Content = JsonContent.Create(new { institutionId = "inst-1", studentUserId = "student-1" })
                };
                request.Headers.Authorization = CreateBearerHeader(
                    new Claim("role", "InstitutionAdmin"),
                    new Claim("institution", "inst-1"),
                    new Claim("sub", "admin-1"));
                var response = await client.SendAsync(request);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
            }
        } finally {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
        }
    }

    [Fact]
    public async Task BearerOnly_ShouldMapInstructorReviewer_WithInstitutionScope() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        const string operatorKey = "bootstrap-operator-key-1234";
        try {
            using var client = CreateClientWithEnv(
                CreateOidcConfig(tempDbPath, operatorKey),
                out var factory,
                ConfigureTestBearerValidation);
            await using (factory) {
                using var createSession = new HttpRequestMessage(HttpMethod.Post, "/sessions") {
                    Content = JsonContent.Create(new { institutionId = "inst-1" })
                };
                createSession.Headers.Add("X-OWS-Verifier-Key", operatorKey);
                var createSessionResponse = await client.SendAsync(createSession);
                createSessionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                using var sessionDocument = JsonDocument.Parse(await createSessionResponse.Content.ReadAsStringAsync());
                var sessionId = sessionDocument.RootElement.GetProperty("sessionId").GetString();

                using var request = new HttpRequestMessage(HttpMethod.Get, $"/sessions/{sessionId}/packages");
                request.Headers.Authorization = CreateBearerHeader(
                    new Claim("role", "InstructorReviewer"),
                    new Claim("institution", "inst-1"),
                    new Claim("sub", "reviewer-1"));
                var response = await client.SendAsync(request);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
            }
        } finally {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
        }
    }

    [Fact]
    public async Task BearerOnly_ShouldMapStudentClient_WithInstitutionAndUserScope() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        const string operatorKey = "bootstrap-operator-key-1234";
        try {
            using var client = CreateClientWithEnv(
                CreateOidcConfig(tempDbPath, operatorKey),
                out var factory,
                ConfigureTestBearerValidation);
            await using (factory) {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/sessions") {
                    Content = JsonContent.Create(new { institutionId = "inst-1", studentUserId = "student-1" })
                };
                request.Headers.Authorization = CreateBearerHeader(
                    new Claim("role", "StudentClient"),
                    new Claim("institution", "inst-1"),
                    new Claim("sub", "student-1"));
                var response = await client.SendAsync(request);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
            }
        } finally {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
        }
    }

    [Fact]
    public async Task BearerOnly_ShouldRejectMissingRoleClaim() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try {
            using var client = CreateClientWithEnv(
                CreateOidcConfig(tempDbPath),
                out var factory,
                ConfigureTestBearerValidation);
            await using (factory) {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/summary");
                request.Headers.Authorization = CreateBearerHeader(new Claim("sub", "user-1"));
                var response = await client.SendAsync(request);
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                var json = await response.Content.ReadAsStringAsync();
                json.Should().Contain("invalid_oidc_claims");
                json.Should().Contain("role claim");
            }
        } finally {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
        }
    }

    [Fact]
    public async Task BearerOnly_ShouldRejectInvalidRoleClaim() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try {
            using var client = CreateClientWithEnv(
                CreateOidcConfig(tempDbPath),
                out var factory,
                ConfigureTestBearerValidation);
            await using (factory) {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/summary");
                request.Headers.Authorization = CreateBearerHeader(
                    new Claim("role", "SuperAdmin"),
                    new Claim("sub", "user-1"));
                var response = await client.SendAsync(request);
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                var json = await response.Content.ReadAsStringAsync();
                json.Should().Contain("invalid_oidc_claims");
                json.Should().Contain("not supported");
            }
        } finally {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
        }
    }

    [Fact]
    public async Task BearerOnly_ShouldRejectMissingInstitutionClaim_ForScopedRole() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        try {
            using var client = CreateClientWithEnv(
                CreateOidcConfig(tempDbPath),
                out var factory,
                ConfigureTestBearerValidation);
            await using (factory) {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/sessions") {
                    Content = JsonContent.Create(new { institutionId = "inst-1", studentUserId = "student-1" })
                };
                request.Headers.Authorization = CreateBearerHeader(
                    new Claim("role", "InstitutionAdmin"),
                    new Claim("sub", "admin-1"));
                var response = await client.SendAsync(request);
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                var json = await response.Content.ReadAsStringAsync();
                json.Should().Contain("invalid_oidc_claims");
                json.Should().Contain("institution claim");
            }
        } finally {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
        }
    }

    [Fact]
    public async Task DualAuth_ShouldReturn400_AndCreateSafeAuditEvent() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        const string operatorKey = "bootstrap-operator-key-1234";
        var bearerToken = CreateBearerToken(
            new Claim("role", "Operator"),
            new Claim("sub", "operator-1"));

        try {
            using var client = CreateClientWithEnv(
                CreateOidcConfig(tempDbPath, operatorKey),
                out var factory,
                ConfigureTestBearerValidation);
            await using (factory) {
                using (var request = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/summary")) {
                    request.Headers.Add("X-OWS-Verifier-Key", operatorKey);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                    var response = await client.SendAsync(request);
                    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                    var json = await response.Content.ReadAsStringAsync();
                    json.Should().Contain("ambiguous_authentication");
                    json.Should().Contain("not both");
                    json.Should().Contain("requestId");
                }

                using var auditRequest = new HttpRequestMessage(HttpMethod.Get, "/audit/events?eventType=auth.ambiguous");
                auditRequest.Headers.Add("X-OWS-Verifier-Key", operatorKey);
                var auditResponse = await client.SendAsync(auditRequest);
                auditResponse.StatusCode.Should().Be(HttpStatusCode.OK);
                var events = await auditResponse.Content.ReadFromJsonAsync<List<VerifierAuditEvent>>();
                events.Should().NotBeNull();
                events.Should().ContainSingle(e => e.EventType == "auth.ambiguous" && e.Result == "DualCredentialsRejected");

                var auditJson = await File.ReadAllTextAsync(Path.Combine(tempDbDir, "audit_events.json"));
                auditJson.Should().NotContain(operatorKey);
                auditJson.Should().NotContain(bearerToken);
            }
        } finally {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
        }
    }

    [Fact]
    public async Task Diagnostics_ShouldExposeSafeOidcStatus_WithoutSecrets() {
        var tempDbDir = Path.Combine(Path.GetTempPath(), $"ows-verifier-{Guid.NewGuid():N}");
        var tempDbPath = Path.Combine(tempDbDir, "receipts.json");
        const string clientSecret = "super-secret-oidc-client-secret";

        try {
            using var client = CreateClientWithEnv(
                CreateOidcConfig(tempDbPath, clientSecret: clientSecret),
                out var factory,
                ConfigureTestBearerValidation);
            await using (factory) {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/diagnostics/summary");
                request.Headers.Authorization = CreateBearerHeader(
                    new Claim("role", "Operator"),
                    new Claim("sub", "operator-1"));
                var response = await client.SendAsync(request);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var text = await response.Content.ReadAsStringAsync();
                text.Should().Contain("\"oidc\"");
                text.Should().Contain("\"enabled\":true");
                text.Should().Contain("\"authorityConfigured\":true");
                text.Should().Contain("\"audienceConfigured\":true");
                text.Should().Contain("\"roleClaimConfigured\":true");
                text.Should().NotContain(clientSecret);
                text.Should().NotContain(SigningKey);
            }
        } finally {
            if (Directory.Exists(tempDbDir)) Directory.Delete(tempDbDir, recursive: true);
        }
    }

    [Fact]
    public void VerifierOidcOptions_CanBeInstantiatedWithAllProperties() {
        var options = new VerifierOidcOptions {
            Enabled = true,
            Authority = "https://authority",
            Audience = "audience",
            ClientId = "client",
            ClientSecret = "secret",
            RequireHttpsMetadata = false,
            RoleClaim = "custom-role",
            InstitutionClaim = "custom-inst",
            UserIdClaim = "custom-sub",
            EmailClaim = "custom-email",
            DisplayNameClaim = "custom-name"
        };

        options.Enabled.Should().BeTrue();
        options.Authority.Should().Be("https://authority");
        options.Audience.Should().Be("audience");
        options.ClientId.Should().Be("client");
        options.ClientSecret.Should().Be("secret");
        options.RequireHttpsMetadata.Should().BeFalse();
        options.RoleClaim.Should().Be("custom-role");
        options.InstitutionClaim.Should().Be("custom-inst");
        options.UserIdClaim.Should().Be("custom-sub");
        options.EmailClaim.Should().Be("custom-email");
        options.DisplayNameClaim.Should().Be("custom-name");
    }
}
