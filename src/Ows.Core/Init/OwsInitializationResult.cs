namespace Ows.Core.Init;

/// <summary>
/// Represents the result of initializing local OWS project state.
/// </summary>
public sealed record OwsInitializationResult {
    /// <summary>
    /// Gets the local OWS folder path that was created or reused.
    /// </summary>
    public string LocalFolderPath { get; init; } = string.Empty;
}
