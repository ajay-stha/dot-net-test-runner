namespace DotNetTestRunner.Domain.Models;

/// <summary>
/// Serializable snapshot of user-selected state that should persist between sessions,
/// such as the last chosen test target and build configuration.
/// </summary>
public sealed class TestRunnerSettings
{
    /// <summary>
    /// Gets or sets the full path to the last selected .sln or .csproj test target.
    /// </summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the last selected build configuration (e.g. Debug, Release, MIQA, UAT).
    /// </summary>
    public string SelectedConfiguration { get; set; } = string.Empty;
}
