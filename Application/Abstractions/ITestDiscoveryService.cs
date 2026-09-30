using DotNetTestRunner.Domain.Models;

namespace DotNetTestRunner.Application.Abstractions;

/// <summary>
/// Turns the flat list of test names reported by <c>dotnet test</c> into entries that carry
/// both a class and a method name, so the tests can be shown as a tree.
/// </summary>
public interface ITestDiscoveryService
{
    /// <summary>
    /// Resolves discovered test names into class and method parts.
    /// </summary>
    /// <param name="discoveredTests">Test names reported by <c>dotnet test</c>.</param>
    /// <param name="targetPath">
    /// Path to the <c>.sln</c> or <c>.csproj</c>, used to scan source files when the reported
    /// names are not fully qualified.
    /// </param>
    /// <returns>One entry per distinct discovered test.</returns>
    Task<IReadOnlyList<TestDiscoveryEntry>> ResolveEntriesAsync(
        IReadOnlyCollection<string> discoveredTests,
        string targetPath);
}
