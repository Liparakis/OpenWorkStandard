using System.Text.RegularExpressions;

namespace Ows.Core.Ignore;

/// <summary>
/// Matches the documented, intentionally small subset of OWS ignore rules.
/// </summary>
public sealed class OwsIgnoreEngine {
    /// <summary>
    /// Gets the default rules created for a new OWS project.
    /// </summary>
    public static IReadOnlyList<string> DefaultPatterns { get; } = [
        ".ows/",
        ".git/",
        "bin/",
        "obj/",
        ".vs/",
        ".idea/",
        ".vscode/",
        "node_modules/",
        "dist/",
        "build/",
        "target/",
        "*.exe",
        "*.dll",
        "*.pdb",
        "*.log",
        ".env",
        ".env.*",
        "secrets.json",
        "*.user",
        "*.suo",
        "__pycache__/",
        "*.pyc",
        ".gradle/",
        "out/",
        "coverage/"
    ];

    /// <summary>
    /// Gets the starter file content written by <c>ows init</c>.
    /// </summary>
    public static string DefaultFileContents { get; } = string.Join(
        Environment.NewLine,
        new[] {
            "# OWS ignore rules: blank lines and # comments are ignored.",
            "# Directory patterns end with /. * and ? are simple wildcards."
        }.Concat(DefaultPatterns)
    ) + Environment.NewLine;

    /// <summary>
    /// The parsed ignore rules.
    /// </summary>
    private readonly IgnoreRule[] _rules;

    /// <summary>
    /// Initializes an ignore engine from rule lines.
    /// </summary>
    /// <param name="ruleLines">Rule lines from an OWS ignore file.</param>
    public OwsIgnoreEngine(IEnumerable<string>? ruleLines = null) {
        _rules = (ruleLines ?? [])
                 .Select(ParseRule)
                 .Where(rule => rule is not null)
                 .Select(rule => rule!)
                 .ToArray();
    }

    /// <summary>
    /// Loads default rules, project rules, and optional configured directory exclusions.
    /// </summary>
    /// <param name="projectRootPath">The project root containing <c>.owsignore</c>.</param>
    /// <param name="additionalDirectoryNames">Additional configured directory names to exclude.</param>
    /// <returns>A loaded ignore engine.</returns>
    public static OwsIgnoreEngine Load(
        string projectRootPath,
        IEnumerable<string>? additionalDirectoryNames = null
    ) {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);

        var rules = new List<string>(DefaultPatterns);
        var ignorePath = Path.Combine(projectRootPath, ".owsignore");
        if (File.Exists(ignorePath)) {
            rules.AddRange(File.ReadLines(ignorePath));
        }

        if (additionalDirectoryNames is not null) {
            rules.AddRange(
                additionalDirectoryNames
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim().EndsWith('/') ? name.Trim() : $"{name.Trim()}/")
            );
        }

        return new OwsIgnoreEngine(rules);
    }

    /// <summary>
    /// Determines whether a root-relative path is ignored.
    /// </summary>
    /// <param name="relativePath">A path relative to the initialized project root.</param>
    /// <param name="isDirectory">Whether the path itself is a directory.</param>
    /// <returns><see langword="true"/> when the path matches an ignore rule.</returns>
    public bool IsIgnored(string relativePath, bool isDirectory = false) {
        var normalizedPath = NormalizeRelativePath(relativePath);
        return normalizedPath is not null && _rules.Any(rule => rule.Matches(normalizedPath, isDirectory));
    }

    /// <summary>
    /// Parses a raw rule string from an ignore file into an IgnoreRule instance.
    /// </summary>
    /// <returns>An <see cref="IgnoreRule"/> representing the parsed rule, or <see langword="null"/> if the rule is empty, a comment, or invalid.</returns>
    /// <param name="rawRule">The raw ignore pattern string to parse.</param>
    private static IgnoreRule? ParseRule(string? rawRule) {
        if (string.IsNullOrWhiteSpace(rawRule)) {
            return null;
        }

        var pattern = rawRule.Trim();
        if (pattern.StartsWith('#') || pattern.StartsWith('!')) {
            return null;
        }

        var rooted = pattern.StartsWith('/');
        pattern = pattern.TrimStart('/').Replace('\\', '/');
        while (pattern.StartsWith("./", StringComparison.Ordinal)) {
            pattern = pattern[2..];
        }

        var directory = pattern.EndsWith('/');
        pattern = pattern.TrimEnd('/');
        return string.IsNullOrWhiteSpace(pattern)
            ? null
            : new IgnoreRule(pattern, rooted, directory);
    }

    /// <summary>
    /// Normalizes a relative path by replacing separators and stripping leading/trailing slashes.
    /// </summary>
    /// <returns>The normalized path string, or <see langword="null"/> if the path is invalid or references parent directories.</returns>
    /// <param name="relativePath">The relative path to normalize.</param>
    private static string? NormalizeRelativePath(string relativePath) {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) {
            return null;
        }

        var normalized = relativePath.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) {
            normalized = normalized[2..];
        }

        return normalized is "." or ".." || normalized.StartsWith("../", StringComparison.Ordinal)
            ? null
            : normalized.Trim('/');
    }

    /// <summary>
    /// Represents the <see cref="IgnoreRule"/> type.
    /// </summary>
    private sealed class IgnoreRule {
        /// <summary>
        /// The regular expression used to evaluate path matches for this rule.
        /// </summary>
        private readonly Regex _regex;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnoreRule"/> class.
        /// </summary>
        /// <param name="pattern">The rule pattern.</param>
        /// <param name="rooted">Indicates if the pattern is anchored to the root path.</param>
        /// <param name="directory">Indicates if the pattern matches directories only.</param>
        public IgnoreRule(string pattern, bool rooted, bool directory) {
            Pattern = pattern;
            Rooted = rooted;
            Directory = directory;
            HasSlash = pattern.Contains('/');
            _regex = new Regex(BuildRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Gets the <see cref="Pattern"/> value.
        /// </summary>
        private string Pattern { get; }
        /// <summary>
        /// Gets the <see cref="Rooted"/> value.
        /// </summary>
        private bool Rooted { get; }
        /// <summary>
        /// Gets the <see cref="Directory"/> value.
        /// </summary>
        private bool Directory { get; }
        /// <summary>
        /// Gets the <see cref="HasSlash"/> value.
        /// </summary>
        private bool HasSlash { get; }

        /// <summary>
        /// Evaluates if this ignore rule matches the specified normalized path.
        /// </summary>
        /// <returns><see langword="true"/> if the rule matches the path; otherwise, <see langword="false"/>.</returns>
        /// <param name="normalizedPath">The normalized path to check.</param>
        /// <param name="isDirectory">Whether the target path points to a directory.</param>
        public bool Matches(string normalizedPath, bool isDirectory) {
            var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) {
                return false;
            }

            if (Directory) {
                if (HasSlash) {
                    var patternSegments = Pattern.Split('/');
                    if (segments.Length < patternSegments.Length) {
                        return false;
                    }

                    var candidate = string.Join('/', segments.Take(patternSegments.Length));
                    return _regex.IsMatch(candidate) &&
                           (isDirectory
                               ? segments.Length == patternSegments.Length
                               : segments.Length > patternSegments.Length);
                }

                var candidateSegments = Rooted ? segments.Take(1) : segments;
                return isDirectory
                    ? candidateSegments.Any(segment => _regex.IsMatch(segment))
                    : candidateSegments.Take(Math.Max(0, segments.Length - 1)).Any(segment => _regex.IsMatch(segment));
            }

            if (HasSlash) {
                return _regex.IsMatch(normalizedPath);
            }

            var fileSegments = Rooted ? segments.Take(1) : segments;
            return fileSegments.Any(segment => _regex.IsMatch(segment));
        }

        /// <summary>
        /// Converts a wildcard pattern into a matching regular expression string.
        /// </summary>
        /// <returns>A regular expression string representation of the pattern.</returns>
        /// <param name="pattern">The raw pattern containing wildcards.</param>
        private static string BuildRegex(string pattern) {
            var escaped = Regex.Escape(pattern)
                               .Replace("\\*", "[^/]*", StringComparison.Ordinal)
                               .Replace("\\?", "[^/]", StringComparison.Ordinal);
            return $"^{escaped}$";
        }
    }
}
