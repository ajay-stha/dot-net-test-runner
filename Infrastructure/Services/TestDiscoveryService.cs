using DotNetTestRunner.Application.Abstractions;
using DotNetTestRunner.Domain.Models;
using DotNetTestRunner.Domain.Services;

namespace DotNetTestRunner.Infrastructure.Services;

/// <summary>
/// Resolves the flat test names reported by <c>dotnet test</c> into class and method parts.
/// Fully qualified names are split directly; bare method names are matched against the
/// methods found in the target's source files.
/// </summary>
public sealed class TestDiscoveryService : ITestDiscoveryService
{
    private const string UNGROUPED_CLASS_NAME = "Ungrouped";

    private readonly ISourceTestIndexer _SourceTestIndexer;

    public TestDiscoveryService(ISourceTestIndexer sourceTestIndexer)
    {
        _SourceTestIndexer = sourceTestIndexer;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TestDiscoveryEntry>> ResolveEntriesAsync(
        IReadOnlyCollection<string> discoveredTests,
        string targetPath)
    {
        var normalized = discoveredTests
            .Select(static test => test.Trim())
            .Where(static test => !string.IsNullOrWhiteSpace(test))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            return [];
        }

        if (normalized.Any(static test => test.Contains('.')))
        {
            return normalized.Select(SplitFullyQualifiedName).ToList();
        }

        var sourceIndex = await Task.Run(() => _SourceTestIndexer.BuildIndex(targetPath));

        if (sourceIndex.Count == 0)
        {
            return normalized.Select(CreateUngroupedEntry).ToList();
        }

        return MatchAgainstSourceIndex(normalized, sourceIndex);
    }

    /// <summary>
    /// Pairs each reported method name with a source method of the same name. Names are
    /// consumed from a queue so overloads and same-named tests in different classes are each
    /// matched once instead of all collapsing onto the first match.
    /// </summary>
    /// <param name="reportedTests">Distinct test names reported by <c>dotnet test</c>.</param>
    /// <param name="sourceIndex">Test methods found in the target's source files.</param>
    /// <returns>One entry per reported test.</returns>
    private static List<TestDiscoveryEntry> MatchAgainstSourceIndex(
        IReadOnlyCollection<string> reportedTests,
        IReadOnlyList<SourceTestMethod> sourceIndex)
    {
        var methodsByName = sourceIndex
            .GroupBy(static method => method.MethodName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => new Queue<SourceTestMethod>(
                    group.OrderBy(static method => method.ClassName, StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

        var resolvedEntries = new List<TestDiscoveryEntry>(reportedTests.Count);

        foreach (var reportedTest in reportedTests)
        {
            var methodKey = NormalizeReportedMethodName(reportedTest);

            if (methodsByName.TryGetValue(methodKey, out var candidates) && candidates.Count > 0)
            {
                var matched = candidates.Dequeue();
                resolvedEntries.Add(new TestDiscoveryEntry(
                    matched.ClassName,
                    matched.MethodName,
                    matched.FullyQualifiedName));

                continue;
            }

            resolvedEntries.Add(CreateUngroupedEntry(reportedTest));
        }

        return resolvedEntries;
    }

    /// <summary>
    /// Splits a fully qualified name into its class and method parts.
    /// </summary>
    /// <param name="fullyQualifiedName">Name to split.</param>
    /// <returns>The resolved entry, or an ungrouped entry when the name has no class part.</returns>
    private static TestDiscoveryEntry SplitFullyQualifiedName(string fullyQualifiedName)
    {
        var lastSeparatorIndex = fullyQualifiedName.LastIndexOf('.');

        if (lastSeparatorIndex <= 0 || lastSeparatorIndex == fullyQualifiedName.Length - 1)
        {
            return CreateUngroupedEntry(fullyQualifiedName);
        }

        return new TestDiscoveryEntry(
            fullyQualifiedName[..lastSeparatorIndex],
            fullyQualifiedName[(lastSeparatorIndex + 1)..],
            fullyQualifiedName);
    }

    /// <summary>
    /// Builds an entry for a test whose class could not be determined, grouping it under the
    /// prefix before the first underscore so related tests still appear together.
    /// </summary>
    /// <param name="reportedTest">Reported test name.</param>
    /// <returns>A best-effort entry.</returns>
    private static TestDiscoveryEntry CreateUngroupedEntry(string reportedTest)
    {
        var firstSeparatorIndex = reportedTest.IndexOf('_');
        var className = firstSeparatorIndex > 0 ? reportedTest[..firstSeparatorIndex] : UNGROUPED_CLASS_NAME;

        return new TestDiscoveryEntry(className, reportedTest, reportedTest);
    }

    /// <summary>
    /// Reduces a reported test name to the bare method name used to look it up in source.
    /// </summary>
    /// <param name="reportedTest">Reported test name.</param>
    /// <returns>The method name without namespace or parameter suffix.</returns>
    private static string NormalizeReportedMethodName(string reportedTest)
    {
        var method = TestNameMatcher.StripParameterSuffix(reportedTest);
        var lastSeparatorIndex = method.LastIndexOf('.');

        if (lastSeparatorIndex >= 0 && lastSeparatorIndex < method.Length - 1)
        {
            method = method[(lastSeparatorIndex + 1)..];
        }

        return method.Trim();
    }
}
