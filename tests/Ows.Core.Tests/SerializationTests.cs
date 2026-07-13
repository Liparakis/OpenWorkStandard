using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Ows.Core.Events;
using Ows.Core.Packaging;

namespace Ows.Core.Tests;

/// <summary>
///     Tests JSON serialization for core models.
/// </summary>
public sealed class SerializationTests {
    /// <summary>
    ///     Serialization options configured for event string-based enum converters in tests.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    ///     Verifies that events serialize and deserialize cleanly.
    /// </summary>
    [Fact]
    public void OwsEvent_ShouldRoundTripThroughJson() {
        var source = new OwsEvent {
            EventType = OwsEventType.FileModified,
            ProjectId = "sample-project",
            RelativePath = "src/Program.cs",
            ToolName = "cli",
            HashAfter = "abc123",
            PreviousEventHash = "prev",
            EventHash = "current",
            Metadata = new Dictionary<string, string> { ["reason"] = "save" }
        };

        var json = JsonSerializer.Serialize(source, SerializerOptions);
        var restored = JsonSerializer.Deserialize<OwsEvent>(json, SerializerOptions);

        restored.Should().NotBeNull();
        restored!.EventType.Should().Be(OwsEventType.FileModified);
        restored.ProjectId.Should().Be("sample-project");
        restored.PreviousEventHash.Should().Be("prev");
        restored.EventHash.Should().Be("current");
        restored.Metadata.Should().ContainKey("reason");
    }

    /// <summary>
    ///     Verifies that manifests serialize and deserialize cleanly.
    /// </summary>
    [Fact]
    public void OwsManifest_ShouldRoundTripThroughJson() {
        var source = new OwsManifest {
            OwsVersion = "0.1",
            PackageId = "pkg-001",
            ProjectName = "sample-project",
            Platform = "windows",
            Toolchain = ".NET 9",
            TrackedPath = "samples/sample-project"
        };

        var json = JsonSerializer.Serialize(source, SerializerOptions);
        var restored = JsonSerializer.Deserialize<OwsManifest>(json, SerializerOptions);

        restored.Should().NotBeNull();
        restored!.PackageId.Should().Be("pkg-001");
        restored.ProjectName.Should().Be("sample-project");
        restored.Platform.Should().Be("windows");
    }
}
