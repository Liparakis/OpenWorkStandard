using FluentAssertions;
using Ows.Core.Agent;

namespace Ows.Core.Tests;

public sealed class SnapshotHashCalculatorTests {
    [Fact]
    public void ComputeHash_IsDeterministicAcrossFileOrdering() {
        var observedAt = DateTimeOffset.Parse("2026-06-20T12:00:00Z");

        var left = new ObservedSnapshot {
            ObservedAt = observedAt,
            Files = new Dictionary<string, ObservedFileState>(StringComparer.OrdinalIgnoreCase) {
                ["src\\B.cs"] = new() {
                    RelativePath = "src\\B.cs",
                    FileHash = "hash-b",
                    Size = 20,
                    LineCount = 2,
                    LastWriteTime = observedAt.AddMinutes(-1),
                    ObservedAt = observedAt
                },
                ["src\\A.cs"] = new() {
                    RelativePath = "src\\A.cs",
                    FileHash = "hash-a",
                    Size = 10,
                    LineCount = 1,
                    LastWriteTime = observedAt.AddMinutes(-2),
                    ObservedAt = observedAt
                }
            }
        };

        var right = new ObservedSnapshot {
            ObservedAt = observedAt,
            Files = new Dictionary<string, ObservedFileState>(StringComparer.OrdinalIgnoreCase) {
                ["src/A.cs"] = new() {
                    RelativePath = "src/A.cs",
                    FileHash = "hash-a",
                    Size = 10,
                    LineCount = 1,
                    LastWriteTime = observedAt.AddMinutes(-2),
                    ObservedAt = observedAt
                },
                ["src/B.cs"] = new() {
                    RelativePath = "src/B.cs",
                    FileHash = "hash-b",
                    Size = 20,
                    LineCount = 2,
                    LastWriteTime = observedAt.AddMinutes(-1),
                    ObservedAt = observedAt
                }
            }
        };

        SnapshotHashCalculator.ComputeHash(left).Should().Be(SnapshotHashCalculator.ComputeHash(right));
    }
}
