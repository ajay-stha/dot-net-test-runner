namespace DotNetTestRunner.Application.Abstractions;

/// <summary>
/// Reads information about the <c>.sln</c> or <c>.csproj</c> the runner targets.
/// </summary>
public interface ITestTargetService
{
    /// <summary>
    /// Reads the build configurations declared by a target.
    /// </summary>
    /// <param name="targetPath">Path to the <c>.sln</c> or <c>.csproj</c>.</param>
    /// <returns>
    /// The declared configurations, or an empty list when the target declares none or cannot
    /// be read. Callers fall back to their own defaults.
    /// </returns>
    IReadOnlyList<string> ReadConfigurations(string targetPath);

    /// <summary>
    /// Chooses the target to load at startup, preferring a previously used one.
    /// </summary>
    /// <param name="persistedTargetPath">Target saved from the last session, if any.</param>
    /// <returns>
    /// The persisted target when it still exists, otherwise a target discovered by walking up
    /// from the application directory, or <see langword="null"/> when none is found.
    /// </returns>
    string? ResolveInitialTargetPath(string? persistedTargetPath);
}
