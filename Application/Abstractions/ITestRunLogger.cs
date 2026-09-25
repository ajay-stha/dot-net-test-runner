using DotNetTestRunner.Domain.Models;

namespace DotNetTestRunner.Application.Abstractions;

/// <summary>
/// Records application lifecycle events and test run activity to the configured log sinks,
/// capturing what happened, when, and which user account ran it.
/// </summary>
public interface ITestRunLogger
{
    /// <summary>
    /// Logs that the application has started successfully.
    /// </summary>
    void LogApplicationStarted();

    /// <summary>
    /// Logs that the application is shutting down normally.
    /// </summary>
    /// <param name="reason">A short description of why the application is stopping.</param>
    void LogApplicationStopped(string reason);

    /// <summary>
    /// Logs that the application is stopping because of an unhandled error.
    /// </summary>
    /// <param name="exception">The exception that caused the application to stop.</param>
    /// <param name="source">The subsystem that observed the exception (e.g. UI dispatcher, AppDomain).</param>
    void LogApplicationCrashed(Exception exception, string source);

    /// <summary>
    /// Logs that a test run has been requested and is starting.
    /// </summary>
    /// <param name="header">A short description of the run (e.g. "Running all tests").</param>
    /// <param name="targetPath">The .sln or .csproj path under test.</param>
    /// <param name="configuration">The selected build configuration.</param>
    /// <param name="filter">The VSTest filter applied, or <see langword="null"/> when running everything.</param>
    void LogTestRunStarted(string header, string targetPath, string configuration, string? filter);

    /// <summary>
    /// Logs the outcome of a <c>dotnet test</c> invocation.
    /// </summary>
    /// <param name="header">A short description of the run.</param>
    /// <param name="targetPath">The .sln or .csproj path under test.</param>
    /// <param name="configuration">The selected build configuration.</param>
    /// <param name="exitCode">The process exit code (0 indicates overall success).</param>
    /// <param name="duration">How long the run took to complete.</param>
    void LogTestRunCompleted(string header, string targetPath, string configuration, int exitCode, TimeSpan duration);

    /// <summary>
    /// Logs an unexpected failure while attempting to run tests.
    /// </summary>
    /// <param name="header">A short description of the run.</param>
    /// <param name="targetPath">The .sln or .csproj path under test.</param>
    /// <param name="configuration">The selected build configuration.</param>
    /// <param name="exception">The exception that interrupted the run.</param>
    void LogTestRunError(string header, string targetPath, string configuration, Exception exception);

    /// <summary>
    /// Logs which individual tests succeeded and which failed for a completed run, including
    /// the failure cause reported by the test framework for each failed test when available.
    /// </summary>
    /// <param name="header">A short description of the run.</param>
    /// <param name="passedCount">The number of tests that passed.</param>
    /// <param name="failedCount">The number of tests that failed.</param>
    /// <param name="failedTests">The failed tests and their failure causes, when available.</param>
    void LogTestRunSummary(string header, int passedCount, int failedCount, IReadOnlyCollection<FailedTestDetail> failedTests);
}
