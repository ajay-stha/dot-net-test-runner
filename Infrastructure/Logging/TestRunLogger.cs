using DotNetTestRunner.Application.Abstractions;
using DotNetTestRunner.Domain.Models;
using Serilog;

namespace DotNetTestRunner.Infrastructure.Logging;

/// <summary>
/// <see cref="ITestRunLogger"/> implementation that writes structured entries through the
/// process-wide Serilog logger configured by <see cref="AppLoggerBootstrapper"/>.
/// </summary>
public sealed class TestRunLogger : ITestRunLogger
{
    private readonly ILogger _Logger = Log.ForContext<TestRunLogger>();

    /// <inheritdoc />
    public void LogApplicationStarted()
    {
        _Logger.Information("Application started.");
    }

    /// <inheritdoc />
    public void LogApplicationStopped(string reason)
    {
        _Logger.Information("Application stopped. Reason: {Reason}", reason);
    }

    /// <inheritdoc />
    public void LogApplicationCrashed(Exception exception, string source)
    {
        _Logger.Fatal(exception, "Application stopped unexpectedly. Source: {Source}", source);
    }

    /// <inheritdoc />
    public void LogTestRunStarted(string header, string targetPath, string configuration, string? filter)
    {
        _Logger.Information(
            "Test run started. Run: {Header}, Target: {TargetPath}, Configuration: {Configuration}, Filter: {Filter}",
            header,
            targetPath,
            configuration,
            filter ?? "(none)");
    }

    /// <inheritdoc />
    public void LogTestRunCompleted(string header, string targetPath, string configuration, int exitCode, TimeSpan duration)
    {
        if (exitCode == 0)
        {
            _Logger.Information(
                "Test run succeeded. Run: {Header}, Target: {TargetPath}, Configuration: {Configuration}, DurationMs: {DurationMs}",
                header,
                targetPath,
                configuration,
                duration.TotalMilliseconds);
            return;
        }

        _Logger.Warning(
            "Test run failed. Run: {Header}, Target: {TargetPath}, Configuration: {Configuration}, ExitCode: {ExitCode}, DurationMs: {DurationMs}",
            header,
            targetPath,
            configuration,
            exitCode,
            duration.TotalMilliseconds);
    }

    /// <inheritdoc />
    public void LogTestRunError(string header, string targetPath, string configuration, Exception exception)
    {
        _Logger.Error(
            exception,
            "Test run failed unexpectedly. Run: {Header}, Target: {TargetPath}, Configuration: {Configuration}",
            header,
            targetPath,
            configuration);
    }

    /// <inheritdoc />
    public void LogTestRunSummary(string header, int passedCount, int failedCount, IReadOnlyCollection<FailedTestDetail> failedTests)
    {
        if (failedCount == 0)
        {
            _Logger.Information(
                "Test run summary for {Header}: {PassedCount} passed, {FailedCount} failed.",
                header,
                passedCount,
                failedCount);
            return;
        }

        _Logger.Warning(
            "Test run summary for {Header}: {PassedCount} passed, {FailedCount} failed.",
            header,
            passedCount,
            failedCount);

        foreach (var failedTest in failedTests)
        {
            _Logger.Error(
                "Test failed. Run: {Header}, Test: {TestName}, Cause: {Cause}",
                header,
                failedTest.FullyQualifiedName,
                failedTest.FailureReason ?? "(no failure details captured)");
        }
    }
}
