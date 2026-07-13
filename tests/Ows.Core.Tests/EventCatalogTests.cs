using FluentAssertions;
using Ows.Core.Events;

namespace Ows.Core.Tests;

/// <summary>
/// Verifies the OWS event catalog and active/reserved mapping integrity.
/// </summary>
public sealed class EventCatalogTests {
    private static string FindCatalogPath(string filename) {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current)) {
            var docsPath = Path.Combine(current, "docs");
            if (Directory.Exists(docsPath)) {
                var directPath = Path.Combine(docsPath, filename);
                if (File.Exists(directPath)) {
                    return directPath;
                }

                var recursivePath = Directory
                    .EnumerateFiles(docsPath, filename, SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (recursivePath is not null) {
                    return recursivePath;
                }
            }

            var parent = Path.GetDirectoryName(current);
            if (parent == current) {
                break;
            }

            current = parent;
        }

        throw new FileNotFoundException($"Could not find {filename} up from {AppContext.BaseDirectory}");
    }

    [Fact]
    public void EventCatalog_ShouldDocumentEveryOwsEventTypeWithCorrectStatus() {
        var catalogPath = FindCatalogPath("EVENT_CATALOG.md");
        var catalogText = File.ReadAllText(catalogPath);

        foreach (OwsEventType type in Enum.GetValues<OwsEventType>()) {
            var typeName = type.ToString();

            // Verify that the event type is documented in the file
            catalogText.Should().Contain(typeName, $"Event type '{typeName}' must be documented in EVENT_CATALOG.md");

            // Verify active vs reserved matching
            // Verify active vs reserved matching
            var isActive = type is OwsEventType.FileCreated
                or OwsEventType.FileModified
                or OwsEventType.FileDeleted
                or OwsEventType.PackageCreated
                or OwsEventType.ProjectOpened
                or OwsEventType.ProjectClosed
                or OwsEventType.BuildStarted
                or OwsEventType.BuildSucceeded
                or OwsEventType.BuildFailed
                or OwsEventType.ProgramExecuted
                or OwsEventType.TestExecuted
                or OwsEventType.WatcherStarted
                or OwsEventType.WatcherStopped
                or OwsEventType.WatcherInterrupted
                or OwsEventType.WatcherRecovered
                or OwsEventType.ObservationGapDetected
                or OwsEventType.UnobservedChangeDetected
                or OwsEventType.LargeUnobservedChangeDetected
                or OwsEventType.SnapshotUpdated;
            if (isActive) {
                catalogText.Should().Contain($"`{typeName}`",
                    $"'{typeName}' is an active event type and must be documented in EVENT_CATALOG.md");
            } else {
                catalogText.Should().Contain($"`{typeName}` is reserved",
                    $"'{typeName}' is reserved and must be documented as such in EVENT_CATALOG.md");
            }
        }
    }

    [Fact]
    public void EventSchema_ShouldListEveryOwsEventType() {
        var schemaPath = FindCatalogPath("EVENT_SCHEMA.md");
        var schemaText = File.ReadAllText(schemaPath);

        foreach (OwsEventType type in Enum.GetValues<OwsEventType>()) {
            var typeName = type.ToString();
            schemaText.Should().Contain($"`{typeName}`", $"Event type '{typeName}' must be listed in EVENT_SCHEMA.md");
        }
    }
}
