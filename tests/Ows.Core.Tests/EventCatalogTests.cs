using FluentAssertions;
using Ows.Core.Events;

namespace Ows.Core.Tests;

/// <summary>
/// Verifies the OWS event catalog and active/reserved mapping integrity.
/// </summary>
public sealed class EventCatalogTests
{
    private static string FindCatalogPath(string filename)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var path = Path.Combine(current, "docs", filename);
            if (File.Exists(path))
            {
                return path;
            }

            var parent = Path.GetDirectoryName(current);
            if (parent == current)
            {
                break;
            }

            current = parent;
        }

        throw new FileNotFoundException($"Could not find {filename} up from {AppContext.BaseDirectory}");
    }

    [Fact]
    public void EventCatalog_ShouldDocumentEveryOwsEventTypeWithCorrectStatus()
    {
        var catalogPath = FindCatalogPath("EVENT_CATALOG.md");
        var catalogText = File.ReadAllText(catalogPath);

        foreach (OwsEventType type in Enum.GetValues<OwsEventType>())
        {
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
                or OwsEventType.TestExecuted;
            if (isActive)
            {
                // Should be documented as Active
                catalogText.Should().Contain($"| `{typeName}` | **Active**",
                    $"'{typeName}' is an active event type and must be marked Active in EVENT_CATALOG.md");
            }
            else
            {
                // Should be documented as Reserved
                catalogText.Should().Contain($"| `{typeName}` | **Reserved**",
                    $"'{typeName}' is a reserved event type and must be marked Reserved in EVENT_CATALOG.md");
            }
        }
    }

    [Fact]
    public void EventSchema_ShouldListEveryOwsEventType()
    {
        var schemaPath = FindCatalogPath("EVENT_SCHEMA.md");
        var schemaText = File.ReadAllText(schemaPath);

        foreach (OwsEventType type in Enum.GetValues<OwsEventType>())
        {
            var typeName = type.ToString();
            schemaText.Should().Contain($"`{typeName}`", $"Event type '{typeName}' must be listed in EVENT_SCHEMA.md");
        }
    }
}