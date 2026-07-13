using System.CommandLine;
using System.IO.Compression;
using System.Text.Json;
using Ows.Core;
using Ows.Core.Events;
using Ows.Core.Packaging;
using Ows.Core.Verification;

namespace Ows.Cli.Commands;

/// <summary>
/// Builds the local reviewer-focused inspect command.
/// </summary>
public static class InspectCommandBuilder {
    /// <summary>
    /// Builds the inspect command.
    /// </summary>
    public static Command Build() {
        var command = new Command("inspect", "Inspect a local OWS package and its review signals.");
        var packageArgument = new Argument<string?>("package") {
            Description = "Path to the local .owspkg file; defaults to the current project package.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var packagePathOption = new Option<string?>("--package-path") {
            Description = "Path to the local .owspkg file; defaults to the current project package."
        };
        var jsonOption = new Option<bool>("--json") {
            Description = "Write the inspection as JSON."
        };
        command.Arguments.Add(packageArgument);
        command.Options.Add(packagePathOption);
        command.Options.Add(jsonOption);
        command.SetAction(async parseResult => {
            var projectRoot = Directory.GetCurrentDirectory();
            var packagePath = parseResult.GetValue(packageArgument) ?? parseResult.GetValue(packagePathOption) ??
                              Path.Combine(projectRoot,
                                  $"{new DirectoryInfo(projectRoot).Name}{OwsConstants.PackageExtension}");
            var result = await new OwsPackageVerifier().VerifyAsync(new PackageVerificationRequest {
                PackagePath = packagePath
            }, CancellationToken.None);
            OwsManifest? manifest = null;
            var artifacts = Array.Empty<ArtifactInspection>();
            var timelineEvents = Array.Empty<TimelineInspectionEvent>();
            var activityPeriods = Array.Empty<ActivityPeriod>();
            string? archiveError = null;
            try {
                if (File.Exists(packagePath)) {
                    using var archive = ZipFile.OpenRead(packagePath);
                    manifest = ReadEntry<OwsManifest>(archive, OwsConstants.ManifestFileName);
                    var artifactEntries = archive.Entries
                        .Where(entry => entry.FullName.StartsWith("artifacts/", StringComparison.Ordinal) &&
                                        !string.IsNullOrEmpty(entry.Name))
                        .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
                        .ToArray();
                    artifacts = artifactEntries.Select(entry => new ArtifactInspection(
                        entry.FullName["artifacts/".Length..],
                        entry.Length,
                        manifest?.ArtifactHashes.GetValueOrDefault(entry.FullName) ?? string.Empty
                    )).ToArray();

                    var events = ReadTimelineEvents(archive);
                    timelineEvents = events.Select(eventRecord => new TimelineInspectionEvent(
                        eventRecord.TimestampUtc, eventRecord.EventType.ToString(), eventRecord.RelativePath)).ToArray();
                    activityPeriods = InferActivityPeriods(events).ToArray();
                }
            } catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException) {
                archiveError = ex.Message;
            }

            if (parseResult.GetValue(jsonOption)) {
                Console.WriteLine(JsonSerializer.Serialize(new {
                    packagePath,
                    manifest,
                    status = result.TrustStatus.ToString(),
                    signatureStatus = result.SignatureStatus,
                    packageRootHash = result.Package.PackageRootHash,
                    artifacts,
                    timeline = result.Timeline,
                    timelineEvents,
                    inferredActivityPeriods = activityPeriods,
                    findings = result.Findings,
                    errors = result.Errors,
                    archiveError
                }, new JsonSerializerOptions { WriteIndented = true }));
            } else {
                Console.WriteLine($"Package: {packagePath}");
                Console.WriteLine($"Manifest: {(manifest is null ? "unavailable" : manifest.PackageId)}");
                Console.WriteLine($"Trust status: {result.TrustStatus}");
                Console.WriteLine($"Signature status: {result.SignatureStatus}");
                Console.WriteLine($"Package root: {result.Package.PackageRootHash}");
                Console.WriteLine($"Artifacts: {artifacts.Length}");
                foreach (var artifact in artifacts) {
                    Console.WriteLine($"Artifact: {artifact.Path} ({artifact.Size} bytes, sha256={artifact.Hash})");
                }
                Console.WriteLine($"Timeline: {result.Timeline.Integrity} ({result.Timeline.EventCount} events)");
                Console.WriteLine($"Inferred activity periods: {activityPeriods.Length}");
                if (archiveError is not null) {
                    Console.WriteLine($"Archive inspection error: {archiveError}");
                }
                foreach (var finding in result.Findings) {
                    Console.WriteLine($"Finding: {finding.Code} - {finding.Title}");
                }
                foreach (var error in result.Errors) {
                    Console.WriteLine($"Error: {error}");
                }
            }

            return result.IsSuccess ? 0 : 1;
        });
        return command;
    }

    private static T? ReadEntry<T>(ZipArchive archive, string entryName) {
        var entry = archive.GetEntry(entryName);
        if (entry is null) {
            return default;
        }

        using var reader = new StreamReader(entry.Open());
        return JsonSerializer.Deserialize<T>(reader.ReadToEnd());
    }

    private static IReadOnlyList<OwsEvent> ReadTimelineEvents(ZipArchive archive) {
        var entry = archive.GetEntry(OwsConstants.TimelineFileName);
        if (entry is null) {
            return [];
        }

        using var reader = new StreamReader(entry.Open());
        var events = new List<OwsEvent>();
        while (reader.ReadLine() is { } line) {
            if (!string.IsNullOrWhiteSpace(line)) {
                events.Add(JsonSerializer.Deserialize<OwsEvent>(line) ??
                           throw new JsonException("Timeline event deserialized to null."));
            }
        }

        return events;
    }

    private static IReadOnlyList<ActivityPeriod> InferActivityPeriods(IReadOnlyList<OwsEvent> events) {
        const double inactivityMinutes = 30;
        var ordered = events.OrderBy(eventRecord => eventRecord.TimestampUtc).ToArray();
        var periods = new List<ActivityPeriod>();
        foreach (var eventRecord in ordered) {
            if (periods.Count == 0 ||
                eventRecord.TimestampUtc - periods[^1].End > TimeSpan.FromMinutes(inactivityMinutes)) {
                periods.Add(new ActivityPeriod(eventRecord.TimestampUtc, eventRecord.TimestampUtc, 1));
                continue;
            }

            periods[^1] = periods[^1] with {
                End = eventRecord.TimestampUtc,
                EventCount = periods[^1].EventCount + 1
            };
        }

        return periods;
    }

    private sealed record ArtifactInspection(string Path, long Size, string Hash);

    private sealed record TimelineInspectionEvent(
        DateTimeOffset TimestampUtc,
        string EventType,
        string? RelativePath);

    private sealed record ActivityPeriod(
        DateTimeOffset Start,
        DateTimeOffset End,
        int EventCount);
}
