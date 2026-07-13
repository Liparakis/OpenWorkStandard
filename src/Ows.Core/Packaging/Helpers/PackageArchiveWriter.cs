using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace Ows.Core.Packaging;

internal static class PackageArchiveWriter {
    public static void WriteArchive(
        string outputPackagePath,
        string projectRootPath,
        OwsManifest manifest,
        string timelineText,
        string versionGraphText,
        string receiptsPath,
        string sessionPath,
        bool hasSession,
        Dictionary<string, string> artifactHashes,
        OwsPackageSignature? signature) {
        if (File.Exists(outputPackagePath)) {
            File.Delete(outputPackagePath);
        }

        using var archive = ZipFile.Open(outputPackagePath, ZipArchiveMode.Create);

        var manifestEntry = archive.CreateEntry(OwsConstants.ManifestFileName);
        using (var writer = new StreamWriter(manifestEntry.Open())) {
            writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }

        var timelineEntry = archive.CreateEntry(OwsConstants.TimelineFileName);
        using (var timelineWriter = new StreamWriter(timelineEntry.Open())) {
            timelineWriter.Write(timelineText);
        }

        var graphEntry = archive.CreateEntry(OwsConstants.VersionGraphFileName);
        using (var graphWriter = new StreamWriter(graphEntry.Open())) {
            graphWriter.Write(versionGraphText);
        }

        if (File.Exists(receiptsPath)) {
            archive.CreateEntryFromFile(receiptsPath, OwsConstants.ReceiptsFileName);
        }

        if (hasSession && File.Exists(sessionPath)) {
            archive.CreateEntryFromFile(sessionPath, OwsConstants.SessionFileName);
        }

        if (signature is not null) {
            var signatureEntry = archive.CreateEntry(OwsConstants.SignatureFileName);
            using var signatureWriter = new StreamWriter(signatureEntry.Open());
            signatureWriter.Write(JsonSerializer.Serialize(signature, new JsonSerializerOptions { WriteIndented = true }));
        }

        foreach (var artifactPath in artifactHashes.Keys) {
            var relativePath = artifactPath["artifacts/".Length..].Replace('/', Path.DirectorySeparatorChar);
            archive.CreateEntryFromFile(Path.Combine(projectRootPath, relativePath), artifactPath);
        }
    }
}
