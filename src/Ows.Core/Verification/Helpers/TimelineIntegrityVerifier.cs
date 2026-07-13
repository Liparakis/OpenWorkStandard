using System.IO.Compression;
using System.Text.Json;
using Ows.Core.Events;

namespace Ows.Core.Verification.Helpers;

/// <summary>
///     Verification helper that analyzes the timeline integrity, validating parent-child event chains and checksum
///     correctness.
/// </summary>
internal static class TimelineIntegrityVerifier {
    /// <summary>
    ///     Parses the timeline file and validates event chaining, matching parent hashes, and event hashes.
    /// </summary>
    /// <param name="archive">The ZIP package container containing the timeline file.</param>
    /// <param name="errors">The list to accumulate verification errors.</param>
    /// <param name="validatedEvents">Output list containing all validated events.</param>
    /// <returns>The hash of the last event in the timeline chain, or genesis if broken/missing.</returns>
    public static string ValidateTimeline(
        ZipArchive archive,
        List<string> errors,
        out List<OwsEvent> validatedEvents
    ) {
        validatedEvents = [];
        var entry = archive.GetEntry(OwsConstants.TimelineFileName);
        if (entry is null) {
            errors.Add($"Missing required entry: {OwsConstants.TimelineFileName}");
            return OwsEventChain.GenesisPreviousEventHash;
        }

        using var reader = new StreamReader(entry.Open());
        var lineNumber = 0;
        var expectedPreviousHash = OwsEventChain.GenesisPreviousEventHash;
        var lastEventHash = OwsEventChain.GenesisPreviousEventHash;

        while (reader.ReadLine() is { } line) {
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }

            try {
                var owsEvent = JsonSerializer.Deserialize<OwsEvent>(line)
                               ?? throw new JsonException("Timeline event deserialized to null.");

                if (!string.Equals(owsEvent.PreviousEventHash, expectedPreviousHash, StringComparison.Ordinal)) {
                    errors.Add($"Broken event chain in {OwsConstants.TimelineFileName} at line {lineNumber}");
                    return OwsEventChain.GenesisPreviousEventHash;
                }

                var actualEventHash = OwsEventChain.ComputeEventHash(owsEvent);
                if (!string.Equals(owsEvent.EventHash, actualEventHash, StringComparison.OrdinalIgnoreCase)) {
                    errors.Add($"Invalid event hash in {OwsConstants.TimelineFileName} at line {lineNumber}");
                    return OwsEventChain.GenesisPreviousEventHash;
                }

                validatedEvents.Add(owsEvent);
                expectedPreviousHash = owsEvent.EventHash;
                lastEventHash = owsEvent.EventHash;
            } catch (JsonException) {
                errors.Add($"Invalid JSON in {OwsConstants.TimelineFileName} at line {lineNumber}");
                return OwsEventChain.GenesisPreviousEventHash;
            }
        }

        return lastEventHash;
    }
}
