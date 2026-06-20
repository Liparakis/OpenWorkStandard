using System.Text.Json;
using FluentAssertions;

namespace Ows.Cli.Tests;

public sealed class ObservabilityAssetsTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void ObservabilityFiles_ShouldExist()
    {
        string[] requiredPaths =
        [
            "deploy/compose/docker-compose.observability.yml",
            "deploy/observability/prometheus.yml",
            "deploy/observability/loki.yml",
            "deploy/observability/promtail.yml",
            "deploy/observability/grafana/dashboards/ows-verifier-overview.json",
            "deploy/observability/grafana/provisioning/datasources/prometheus.yml",
            "deploy/observability/grafana/provisioning/dashboards/dashboards.yml",
            "docs/OBSERVABILITY.md"
        ];

        foreach (var relativePath in requiredPaths)
        {
            File.Exists(Path.Combine(RepoRoot, relativePath)).Should().BeTrue(relativePath);
        }
    }

    [Fact]
    public void GrafanaDashboard_ShouldBeValidJson()
    {
        var dashboardPath = Path.Combine(
            RepoRoot,
            "deploy",
            "observability",
            "grafana",
            "dashboards",
            "ows-verifier-overview.json");

        var json = File.ReadAllText(dashboardPath);
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("title").GetString().Should().Be("OWS Verifier Overview");
        document.RootElement.GetProperty("panels").ValueKind.Should().Be(JsonValueKind.Array);
    }
}
