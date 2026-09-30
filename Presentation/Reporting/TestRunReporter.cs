using DotNetTestRunner.Application.Abstractions;
using DotNetTestRunner.Domain.Models;
using DotNetTestRunner.Domain.Services;
using DotNetTestRunner.Presentation.ViewModels;

namespace DotNetTestRunner.Presentation.Reporting;

/// <summary>
/// Writes the run log for a test run, turning the state of the test tree and the captured
/// console output into log records. Keeping this separate lets the view model orchestrate a
/// run without also knowing how runs are reported.
/// </summary>
public sealed class TestRunReporter
{
    private readonly ITestRunLogger _TestRunLogger;
    private readonly ITestOutputParser _TestOutputParser;

    public TestRunReporter(ITestRunLogger testRunLogger, ITestOutputParser testOutputParser)
    {
        _TestRunLogger = testRunLogger;
        _TestOutputParser = testOutputParser;
    }

    /// <summary>
    /// Records that a run has started.
    /// </summary>
    /// <param name="context">The run being reported.</param>
    /// <param name="filter">Test filter used, or <see langword="null"/> when running all tests.</param>
    public void ReportStarted(TestRunContext context, string? filter)
    {
        _TestRunLogger.LogTestRunStarted(context.Header, context.TargetPath, context.Configuration, filter);
    }

    /// <summary>
    /// Records that a run finished on its own.
    /// </summary>
    /// <param name="context">The run being reported.</param>
    /// <param name="exitCode">Exit code reported by <c>dotnet test</c>.</param>
    /// <param name="duration">How long the run lasted.</param>
    public void ReportCompleted(TestRunContext context, int exitCode, TimeSpan duration)
    {
        _TestRunLogger.LogTestRunCompleted(context.Header, context.TargetPath, context.Configuration, exitCode, duration);
    }

    /// <summary>
    /// Records that a run ended before all of its tests reported.
    /// </summary>
    /// <param name="context">The run being reported.</param>
    /// <param name="reason">Why the run ended early.</param>
    /// <param name="duration">How long the run lasted.</param>
    public void ReportStopped(TestRunContext context, RunStopReason reason, TimeSpan duration)
    {
        _TestRunLogger.LogTestRunStopped(
            context.Header,
            context.TargetPath,
            context.Configuration,
            duration,
            DescribeStopReason(reason));
    }

    /// <summary>
    /// Records that a run could not be carried out.
    /// </summary>
    /// <param name="context">The run being reported.</param>
    /// <param name="exception">The failure encountered.</param>
    public void ReportError(TestRunContext context, Exception exception)
    {
        _TestRunLogger.LogTestRunError(context.Header, context.TargetPath, context.Configuration, exception);
    }

    /// <summary>
    /// Records the pass/fail counts for a completed run and, for each failed test, the error
    /// message and stack trace extracted from the captured output when available.
    /// </summary>
    /// <param name="context">The run being reported.</param>
    /// <param name="methods">The tests that were run, carrying their final state.</param>
    /// <param name="capturedLines">Console output captured during the run.</param>
    public void ReportSummary(
        TestRunContext context,
        IReadOnlyCollection<TestMethodNode> methods,
        IReadOnlyCollection<string> capturedLines)
    {
        var passedCount = methods.Count(static methodNode => methodNode.RunState == TestRunState.Passed);
        var failedMethods = methods.Where(static methodNode => methodNode.RunState == TestRunState.Failed).ToList();

        // Parsing the full output is only worth it when something actually failed.
        var failureDetails = failedMethods.Count > 0
            ? _TestOutputParser.ParseFailureDetails(capturedLines)
            : new Dictionary<string, TestFailureDetail>();

        var failedTests = failedMethods
            .Select(methodNode =>
            {
                var detail = FindFailureDetail(methodNode, failureDetails);
                return new FailedTestDetail(methodNode.FullyQualifiedName, detail.ErrorMessage, detail.StackTrace);
            })
            .ToList();

        _TestRunLogger.LogTestRunSummary(context.Header, passedCount, failedMethods.Count, failedTests);
    }

    /// <summary>
    /// Finds the failure captured for a test, matching by the raw names reported in the
    /// console output.
    /// </summary>
    /// <param name="methodNode">Failed test.</param>
    /// <param name="failureDetails">Failures keyed by reported name.</param>
    /// <returns>The matching detail, or an empty detail when none was captured.</returns>
    private static TestFailureDetail FindFailureDetail(
        TestMethodNode methodNode,
        IReadOnlyDictionary<string, TestFailureDetail> failureDetails)
    {
        foreach (var (reportedName, detail) in failureDetails)
        {
            if (TestNameMatcher.Matches(reportedName, methodNode.FullyQualifiedName, methodNode.MethodName))
            {
                return detail;
            }
        }

        return TestFailureDetail.Empty;
    }

    private static string DescribeStopReason(RunStopReason reason)
    {
        return reason switch
        {
            RunStopReason.ApplicationClosed => "Application under test closed",
            RunStopReason.TimedOut => "Timed out",
            RunStopReason.User => "Stopped by user",
            _ => "Stopped"
        };
    }
}
