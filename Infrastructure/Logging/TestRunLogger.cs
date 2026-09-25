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
        _Logger.Information("APP START");
    }

    /// <inheritdoc />
    public void LogApplicationStopped(string reason)
    {
        _Logger.Information("APP STOP | Reason={Reason}", FormatValue(reason));
    }

    /// <inheritdoc />
    public void LogApplicationCrashed(Exception exception, string source)
    {
        _Logger.Fatal(exception, "APP CRASH | Source={Source}", FormatValue(source));
    }

    /// <inheritdoc />
    public void LogTestRunStarted(string header, string targetPath, string configuration, string? filter)
    {
        _Logger.Information(
            "RUN START | Run={Run} | Target={Target} | Config={Config} | Filter={Filter}",
            FormatValue(header),
            FormatValue(targetPath),
            FormatValue(configuration),
            FormatValue(filter ?? "(none)"));
    }

    /// <inheritdoc />
    public void LogTestRunCompleted(string header, string targetPath, string configuration, int exitCode, TimeSpan duration)
    {
        if (exitCode == 0)
        {
            _Logger.Information(
                "RUN END | Status=PASSED | Run={Run} | Target={Target} | Config={Config} | DurationMs={DurationMs:0.##}",
                FormatValue(header),
                FormatValue(targetPath),
                FormatValue(configuration),
                duration.TotalMilliseconds);
            return;
        }

        _Logger.Warning(
            "RUN END | Status=FAILED | Run={Run} | Target={Target} | Config={Config} | ExitCode={ExitCode} | DurationMs={DurationMs:0.##}",
            FormatValue(header),
            FormatValue(targetPath),
            FormatValue(configuration),
            exitCode,
            duration.TotalMilliseconds);
    }

    /// <inheritdoc />
    public void LogTestRunError(string header, string targetPath, string configuration, Exception exception)
    {
        _Logger.Error(
            exception,
            "RUN ERROR | Run={Run} | Target={Target} | Config={Config}",
            FormatValue(header),
            FormatValue(targetPath),
            FormatValue(configuration));
    }

    /// <inheritdoc />
    public void LogTestRunSummary(string header, int passedCount, int failedCount, IReadOnlyCollection<FailedTestDetail> failedTests)
    {
        if (failedCount == 0)
        {
            _Logger.Information(
                "RUN SUMMARY | Run={Run} | Passed={Passed} | Failed={Failed}",
                FormatValue(header),
                passedCount,
                failedCount);
            return;
        }

        _Logger.Warning(
            "RUN SUMMARY | Run={Run} | Passed={Passed} | Failed={Failed}",
            FormatValue(header),
            passedCount,
            failedCount);

        foreach (var failedTest in failedTests)
        {
            _Logger.Error(
                "TEST FAILED | Run={Run} | Test={Test} | Error={Error} | StackTrace={StackTrace}",
                FormatValue(header),
                FormatValue(failedTest.FullyQualifiedName),
                FormatValue(failedTest.ErrorMessage ?? "(no error message captured)"),
                FormatValue(failedTest.StackTrace ?? "(no stack trace captured)"));
        }
    }

    private static string FormatValue(string value)
    {
        return value
            .Replace("\r\n", " | ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}
