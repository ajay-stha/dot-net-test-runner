using DotNetTestRunner.Domain.Models;

namespace DotNetTestRunner.Application.Abstractions;

/// <summary>
/// Extracts structured results from the console output produced by <c>dotnet test</c>.
/// </summary>
public interface ITestOutputParser
{
    /// <summary>
    /// Reads the test names listed by <c>dotnet test --list-tests</c>.
    /// </summary>
    /// <param name="outputLines">Captured console output.</param>
    /// <returns>The discovered test names, in the order they were listed.</returns>
    IReadOnlyList<string> ParseDiscoveredTestNames(IEnumerable<string> outputLines);

    /// <summary>
    /// Reads the raw names of the tests reported as failed.
    /// </summary>
    /// <param name="outputLines">Captured console output.</param>
    /// <returns>The reported names of failed tests.</returns>
    IReadOnlyList<string> ParseFailedTestNames(IEnumerable<string> outputLines);

    /// <summary>
    /// Reads the error message and stack trace reported for each failed test.
    /// </summary>
    /// <param name="outputLines">Captured console output.</param>
    /// <returns>Failure details keyed by the raw reported test name.</returns>
    IReadOnlyDictionary<string, TestFailureDetail> ParseFailureDetails(IEnumerable<string> outputLines);
}
