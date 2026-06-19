using System.Text.Json;

namespace Ows.Core.Init;

/// <summary>
/// Creates the minimal local OWS folder structure for a project.
/// </summary>
public sealed class OwsProjectInitializer
{
    /// <summary>
    /// Initializes local OWS state inside the provided project root.
    /// </summary>
    /// <param name="projectRootPath">The project root path.</param>
    /// <returns>The initialization result.</returns>
    public OwsInitializationResult Initialize(string projectRootPath)
    {
        ArgumentNullException.ThrowIfNull(projectRootPath);

        Directory.CreateDirectory(projectRootPath);

        var localFolderPath = Path.Combine(projectRootPath, OwsConstants.LocalFolderName);
        Directory.CreateDirectory(localFolderPath);

        var configPath = Path.Combine(localFolderPath, "config.json");
        var timelinePath = Path.Combine(localFolderPath, OwsConstants.TimelineFileName);

        var config = new
        {
            owsVersion = "0.1",
            projectRoot = projectRootPath,
            initializedAtUtc = DateTimeOffset.UtcNow
        };

        File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true
        }));

        if (!File.Exists(timelinePath))
        {
            File.WriteAllText(timelinePath, string.Empty);
        }

        return new OwsInitializationResult
        {
            LocalFolderPath = localFolderPath
        };
    }
}