using FluentAssertions;
using Ows.Core.Ignore;

namespace Ows.Core.Tests;

/// <summary>
/// Tests the documented OWS ignore-rule subset.
/// </summary>
public sealed class OwsIgnoreEngineTests {
    [Fact]
    public void DefaultRules_ShouldIgnoreGeneratedFoldersSecretsAndCommonBinaries() {
        var engine = new OwsIgnoreEngine(OwsIgnoreEngine.DefaultPatterns);

        engine.IsIgnored("bin", isDirectory: true).Should().BeTrue();
        engine.IsIgnored("src\\bin\\Debug\\app.dll").Should().BeTrue();
        engine.IsIgnored("logs/run.log").Should().BeTrue();
        engine.IsIgnored(".env.local").Should().BeTrue();
        engine.IsIgnored("src/Program.cs").Should().BeFalse();
    }

    [Fact]
    public void Rules_ShouldSupportCommentsWildcardsDirectoryPatternsAndRootRelativePaths() {
        var engine = new OwsIgnoreEngine([
            "# comment",
            "",
            "generated/",
            "*.tmp",
            "src/generated/"
        ]);

        engine.IsIgnored("generated", isDirectory: true).Should().BeTrue();
        engine.IsIgnored("generated/output.txt").Should().BeTrue();
        engine.IsIgnored("nested/generated/output.txt").Should().BeTrue();
        engine.IsIgnored("notes.tmp").Should().BeTrue();
        engine.IsIgnored("src\\generated\\file.cs").Should().BeTrue();
        var rootRelativeEngine = new OwsIgnoreEngine(["src/generated/"]);
        rootRelativeEngine.IsIgnored("nested/src/generated/file.cs").Should().BeFalse();
        rootRelativeEngine.IsIgnored("src/generated/file.cs").Should().BeTrue();
        engine.IsIgnored("src/main.cs").Should().BeFalse();
    }

    [Fact]
    public void Load_ShouldReadProjectRulesAndRetainConfiguredDirectoryExclusions() {
        var projectRoot = Path.Combine(Path.GetTempPath(), $"ows-ignore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);

        try {
            File.WriteAllText(Path.Combine(projectRoot, ".owsignore"), "custom/\n*.cache\n");

            var engine = OwsIgnoreEngine.Load(projectRoot, ["extra-output"]);

            engine.IsIgnored("custom/file.txt").Should().BeTrue();
            engine.IsIgnored("data/cache.cache").Should().BeTrue();
            engine.IsIgnored("extra-output/file.txt").Should().BeTrue();
            engine.IsIgnored("src/main.cs").Should().BeFalse();
        } finally {
            if (Directory.Exists(projectRoot)) {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }
}
