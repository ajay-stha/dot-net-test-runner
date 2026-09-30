using DotNetTestRunner.Domain.Models;

namespace DotNetTestRunner.Application.Abstractions;

/// <summary>
/// Finds the test methods declared in a target's C# source files.
/// </summary>
public interface ISourceTestIndexer
{
    /// <summary>
    /// Scans the source files belonging to a target for methods carrying a test attribute.
    /// </summary>
    /// <param name="targetPath">Path to the <c>.sln</c> or <c>.csproj</c> being run.</param>
    /// <returns>
    /// The test methods found in source. Empty when the target cannot be read, so callers
    /// fall back to the names reported by <c>dotnet test</c>.
    /// </returns>
    IReadOnlyList<SourceTestMethod> BuildIndex(string targetPath);
}
