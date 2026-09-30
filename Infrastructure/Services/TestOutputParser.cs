using System.Text.RegularExpressions;
using DotNetTestRunner.Application.Abstractions;
using DotNetTestRunner.Domain.Models;

namespace DotNetTestRunner.Infrastructure.Services;

/// <summary>
/// Parses the console output of <c>dotnet test</c>. The output has no machine-readable
/// format, so discovery and failure information is recovered from well-known line prefixes.
/// </summary>
public sealed partial class TestOutputParser : ITestOutputParser
{
    private const string FAILED_PREFIX = "Failed ";
    private const string PASSED_PREFIX = "Passed ";
    private const string ERROR_MESSAGE_HEADING = "Error Message:";
    private const string STACK_TRACE_HEADING = "Stack Trace:";
    private const string TEST_LIST_HEADING = "The following Tests are available";

    /// <summary>
    /// Line prefixes that never introduce a test name in the discovery listing.
    /// </summary>
    private static readonly string[] _NonTestLinePrefixes =
    [
        "Test run",
        "Total tests",
        "Passed!",
        "Build",
        "Restore",
        "Workload",
        "VSTest",
        "Starting"
    ];

    /// <summary>
    /// Line prefixes that end the discovery listing.
    /// </summary>
    private static readonly string[] _TestListTerminators =
    [
        "Test run",
        "Total tests",
        "Workload updates"
    ];

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_+.()]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TestNameRegex();

    /// <inheritdoc />
    public IReadOnlyList<string> ParseDiscoveredTestNames(IEnumerable<string> outputLines)
    {
        var lines = outputLines as IReadOnlyList<string> ?? outputLines.ToList();
        var discoveredTests = new List<string>();
        var isInTestSection = false;

        foreach (var line in lines)
        {
            if (line.Contains(TEST_LIST_HEADING, StringComparison.OrdinalIgnoreCase))
            {
                isInTestSection = true;
                continue;
            }

            if (!isInTestSection || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (StartsWithAny(line, _TestListTerminators))
            {
                break;
            }

            var trimmed = line.Trim();

            if (IsPotentialTestName(trimmed))
            {
                discoveredTests.Add(trimmed);
            }
        }

        if (discoveredTests.Count > 0)
        {
            return discoveredTests;
        }

        // Some adapters omit the heading entirely, so fall back to scanning every line.
        return lines
            .Select(static line => line.Trim())
            .Where(IsPotentialTestName)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ParseFailedTestNames(IEnumerable<string> outputLines)
    {
        var failed = new List<string>();

        foreach (var rawLine in outputLines)
        {
            var reportedName = ReadReportedFailureName(rawLine);

            if (reportedName is not null)
            {
                failed.Add(reportedName);
            }
        }

        return failed;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, TestFailureDetail> ParseFailureDetails(IEnumerable<string> outputLines)
    {
        var lines = outputLines as IReadOnlyList<string> ?? outputLines.ToList();
        var detailsByReportedName = new Dictionary<string, TestFailureDetail>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Count; i++)
        {
            var reportedName = ReadReportedFailureName(lines[i]);

            if (reportedName is null)
            {
                continue;
            }

            var details = ExtractFailureDetail(lines, i + 1);

            if (details.HasContent)
            {
                detailsByReportedName[reportedName] = details;
            }
        }

        return detailsByReportedName;
    }

    /// <summary>
    /// Reads the test name from a "Failed &lt;test&gt; [duration]" line, discarding the
    /// trailing duration.
    /// </summary>
    /// <param name="rawLine">Line to read.</param>
    /// <returns>The reported name, or <see langword="null"/> when the line is not a failure.</returns>
    private static string? ReadReportedFailureName(string rawLine)
    {
        var line = rawLine.Trim();

        if (!line.StartsWith(FAILED_PREFIX, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = line[FAILED_PREFIX.Length..].Trim();
        var durationIndex = rest.IndexOf(" [", StringComparison.Ordinal);

        if (durationIndex >= 0)
        {
            rest = rest[..durationIndex];
        }

        rest = rest.Trim();
        return rest.Length > 0 ? rest : null;
    }

    /// <summary>
    /// Reads the error and stack trace sections that follow a failure line, stopping at the
    /// start of the next test's output.
    /// </summary>
    /// <param name="lines">All captured output lines.</param>
    /// <param name="startIndex">Index of the first line after the failure line.</param>
    /// <returns>The captured failure detail.</returns>
    private static TestFailureDetail ExtractFailureDetail(IReadOnlyList<string> lines, int startIndex)
    {
        var errorMessageLines = new List<string>();
        var stackTraceLines = new List<string>();
        var section = FailureOutputSection.None;

        for (var i = startIndex; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();

            if (trimmed.StartsWith(FAILED_PREFIX, StringComparison.Ordinal)
                || trimmed.StartsWith(PASSED_PREFIX, StringComparison.Ordinal))
            {
                break;
            }

            if (string.Equals(trimmed, ERROR_MESSAGE_HEADING, StringComparison.OrdinalIgnoreCase))
            {
                section = FailureOutputSection.ErrorMessage;
                continue;
            }

            if (string.Equals(trimmed, STACK_TRACE_HEADING, StringComparison.OrdinalIgnoreCase))
            {
                section = FailureOutputSection.StackTrace;
                continue;
            }

            if (trimmed.Length == 0)
            {
                // A blank line ends the stack trace, but only separates paragraphs elsewhere.
                if (section == FailureOutputSection.StackTrace)
                {
                    break;
                }

                continue;
            }

            switch (section)
            {
                case FailureOutputSection.ErrorMessage:
                    errorMessageLines.Add(trimmed);
                    break;

                case FailureOutputSection.StackTrace:
                    stackTraceLines.Add(trimmed);
                    break;
            }
        }

        return new TestFailureDetail(
            errorMessageLines.Count > 0 ? string.Join(" ", errorMessageLines) : null,
            stackTraceLines.Count > 0 ? string.Join(Environment.NewLine, stackTraceLines) : null);
    }

    /// <summary>
    /// Determines whether a discovery line looks like a test name rather than build output.
    /// </summary>
    /// <param name="line">Trimmed line to test.</param>
    /// <returns><see langword="true"/> when the line could name a test.</returns>
    private static bool IsPotentialTestName(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (line.Contains(" -> ", StringComparison.Ordinal)
            || line.Contains('\\')
            || line.Contains(' ')
            || line.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || line.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || StartsWithAny(line, _NonTestLinePrefixes))
        {
            return false;
        }

        return TestNameRegex().IsMatch(line);
    }

    private static bool StartsWithAny(string line, string[] prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Which part of a failure block the parser is currently reading.
    /// </summary>
    private enum FailureOutputSection
    {
        None,
        ErrorMessage,
        StackTrace
    }
}
