using System.Text.Json;
using FluentAssertions;
using Ows.Core.Events;
using Ows.Core.Packaging;
using Ows.Core.Verification;

namespace Ows.Core.Tests;

/// <summary>
///     Verifies deterministic activity-pattern review signals without treating them as misconduct findings.
/// </summary>
public sealed class ActivityPatternAnalyzerTests {
    /// <summary>
    ///     Verifies that a one-shot multi-file timeline reports concentration, bulk creation, compressed duration, and
    ///     low revision density signals.
    /// </summary>
    [Fact]
    public void Analyze_ShouldReportOneShotActivitySignals() {
        var start = DateTimeOffset.UtcNow;
        var events = Enumerable.Range(1, 20)
                                .Select(index => FileEvent(
                                    OwsEventType.FileCreated,
                                    $"src/file{index}.py",
                                    start.AddSeconds(index / 10.0)
                                ))
                                .ToArray();

        var result = OwsActivityPatternAnalyzer.Analyze(events);

        result.Info.MeaningfulEventCount.Should().Be(20);
        result.Info.DistinctFileCount.Should().Be(20);
        result.Info.LargestBurstEventCount.Should().Be(20);
        result.Findings.Select(finding => finding.Code).Should().Contain(
            [
                "activity.burst",
                "activity.bulk_creation",
                "activity.compressed_duration",
                "activity.low_revision_density"
            ]
        );
    }

    /// <summary>
    ///     Verifies that spaced, repeatedly revised work does not produce one-shot activity signals.
    /// </summary>
    [Fact]
    public void Analyze_ShouldNotReportOneShotSignalsForIncrementalWork() {
        var start = DateTimeOffset.UtcNow;
        var events = Enumerable.Range(1, 5)
                                .SelectMany(index => new[] {
                                    FileEvent(
                                        OwsEventType.FileCreated,
                                        $"src/file{index}.py",
                                        start.AddMinutes(index * 10)
                                    ),
                                    FileEvent(
                                        OwsEventType.FileModified,
                                        $"src/file{index}.py",
                                        start.AddMinutes((index * 10) + 5),
                                        1_000
                                    )
                                })
                                .ToArray();

        var result = OwsActivityPatternAnalyzer.Analyze(events);

        result.Findings.Should().NotContain(finding => finding.Code.StartsWith("activity."));
    }

    /// <summary>
    ///     Verifies that the Agent's initial inventory baseline is not treated as student activity.
    /// </summary>
    [Fact]
    public void Analyze_ShouldIgnoreInitialBaselineEvents() {
        var start = DateTimeOffset.UtcNow;
        var events = Enumerable.Range(1, 20)
                                .Select(index => FileEvent(
                                    OwsEventType.FileCreated,
                                    $"src/file{index}.py",
                                    start.AddSeconds(index / 10.0),
                                    metadata: new Dictionary<string, string> {
                                        ["source"] = "initial_baseline",
                                        ["usedForTrust"] = "false"
                                    }
                                ))
                                .ToArray();

        var result = OwsActivityPatternAnalyzer.Analyze(events);

        result.Info.MeaningfulEventCount.Should().Be(0);
        result.Findings.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that a large final rewrite is reported when the file was previously observed.
    /// </summary>
    [Fact]
    public void Analyze_ShouldReportLateRewrite() {
        var start = DateTimeOffset.UtcNow;
        var events = new[] {
            FileEvent(OwsEventType.FileCreated, "src/main.py", start),
            FileEvent(OwsEventType.FileModified, "src/main.py", start.AddMinutes(1), 100),
            FileEvent(OwsEventType.FileCreated, "src/test_main.py", start.AddMinutes(2)),
            FileEvent(OwsEventType.FileModified, "src/test_main.py", start.AddMinutes(3), 100),
            FileEvent(OwsEventType.FileCreated, "README.md", start.AddMinutes(4)),
            FileEvent(OwsEventType.FileModified, "README.md", start.AddMinutes(5), 100),
            FileEvent(OwsEventType.FileCreated, "src/final.py", start.AddMinutes(9)),
            FileEvent(OwsEventType.FileModified, "src/main.py", start.AddMinutes(10), 10_000)
        };

        var result = OwsActivityPatternAnalyzer.Analyze(events);

        result.Findings.Select(finding => finding.Code).Should().Contain("activity.late_rewrite");
    }

    /// <summary>
    ///     Verifies that activity signals flow through package creation and signed offline verification.
    /// </summary>
    /// <returns>A task representing the asynchronous verification test.</returns>
    [Fact]
    public async Task VerifySignedPackage_ShouldExposeActivitySignalsSeparatelyFromTrust() {
        var root = Path.Combine(Path.GetTempPath(), $"ows-activity-{Guid.NewGuid():N}");
        var packagePath = Path.Combine(root, "submission.owspkg");
        var keyPath = Path.Combine(root, "signing-key.json");
        try {
            Directory.CreateDirectory(Path.Combine(root, ".ows"));
            Directory.CreateDirectory(Path.Combine(root, "src"));
            var start = DateTimeOffset.UtcNow;
            var previousHash = OwsEventChain.GenesisPreviousEventHash;
            var timeline = new List<string>();
            foreach (var index in Enumerable.Range(1, 20)) {
                var path = $"src/file{index}.py";
                File.WriteAllText(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)), $"print({index})\n");
                var chained = OwsEventChain.CreateChainedEvent(
                    new OwsEvent {
                        EventType = OwsEventType.FileCreated,
                        ProjectId = "activity-fixture",
                        RelativePath = path,
                        TimestampUtc = start.AddSeconds(index / 10.0)
                    },
                    previousHash
                );
                timeline.Add(JsonSerializer.Serialize(chained));
                previousHash = chained.EventHash;
            }

            File.WriteAllLines(
                Path.Combine(root, ".ows", OwsConstants.TimelineFileName),
                timeline
            );
            await OwsPackageBuilder.CreatePackageAsync(
                new PackageCreationRequest {
                    ProjectRootPath = root,
                    OutputPackagePath = packagePath,
                    SignPackage = true,
                    SigningKeyPath = keyPath
                },
                CancellationToken.None
            );

            var result = await OwsPackageVerifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None
            );

            result.TrustStatus.Should().Be(TrustStatus.Verified);
            result.Activity.MeaningfulEventCount.Should().Be(20);
            result.Findings.Select(finding => finding.Code).Should().Contain("activity.burst");
            result.Recommendation.Should().Be("Cryptographically verified; activity review recommended");
        } finally {
            if (Directory.Exists(root)) {
                Directory.Delete(root, true);
            }
        }
    }

    /// <summary>
    ///     Verifies that an explicit generated-by marker is reported without changing package integrity status.
    /// </summary>
    /// <returns>A task representing the asynchronous verification test.</returns>
    [Fact]
    public async Task VerifySignedPackage_ShouldReportExplicitAuthorshipMarker() {
        var root = Path.Combine(Path.GetTempPath(), $"ows-authorship-{Guid.NewGuid():N}");
        var packagePath = Path.Combine(root, "submission.owspkg");
        var keyPath = Path.Combine(root, "signing-key.json");
        try {
            Directory.CreateDirectory(Path.Combine(root, ".ows"));
            File.WriteAllText(Path.Combine(root, "main.py"), "print('hello')\n");
            File.WriteAllText(Path.Combine(root, "README.md"), "Generated by ChatGPT for demonstration.\n");
            var timelineEvent = OwsEventChain.CreateChainedEvent(
                new OwsEvent {
                    EventType = OwsEventType.FileCreated,
                    ProjectId = "authorship-fixture",
                    RelativePath = "main.py"
                },
                OwsEventChain.GenesisPreviousEventHash
            );
            File.WriteAllText(
                Path.Combine(root, ".ows", OwsConstants.TimelineFileName),
                JsonSerializer.Serialize(timelineEvent) + Environment.NewLine
            );

            await OwsPackageBuilder.CreatePackageAsync(
                new PackageCreationRequest {
                    ProjectRootPath = root,
                    OutputPackagePath = packagePath,
                    SignPackage = true,
                    SigningKeyPath = keyPath
                },
                CancellationToken.None
            );

            var result = await OwsPackageVerifier.VerifyAsync(
                new PackageVerificationRequest { PackagePath = packagePath },
                CancellationToken.None
            );

            result.TrustStatus.Should().Be(TrustStatus.Verified);
            result.Findings.Select(finding => finding.Code).Should().Contain("authorship.explicit_marker");
            result.Recommendation.Should().Be("Cryptographically verified; authorship review recommended");
        } finally {
            if (Directory.Exists(root)) {
                Directory.Delete(root, true);
            }
        }
    }

    /// <summary>
    ///     Creates an unchained event for analyzer input.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <param name="path">The relative file path.</param>
    /// <param name="timestamp">The event timestamp.</param>
    /// <param name="bytesChanged">The optional changed-byte estimate.</param>
    /// <param name="metadata">The optional event metadata.</param>
    /// <returns>A file event containing the supplied activity data.</returns>
    private static OwsEvent FileEvent(
        OwsEventType eventType,
        string path,
        DateTimeOffset timestamp,
        long? bytesChanged = null,
        IReadOnlyDictionary<string, string>? metadata = null
    ) => new() {
        EventType = eventType,
        RelativePath = path,
        TimestampUtc = timestamp,
        BytesChanged = bytesChanged,
        Metadata = metadata ?? new Dictionary<string, string>()
    };
}
