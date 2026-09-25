using System.IO;
using DotNetTestRunner.Domain.Models;
using Serilog;
using Serilog.Events;

namespace DotNetTestRunner.Infrastructure.Logging;

/// <summary>
/// Configures the process-wide Serilog logger used by <see cref="TestRunLogger"/>, including
/// the rolling file sink and log-level/rolling behavior read from <see cref="LoggingSettings"/>.
/// </summary>
public static class AppLoggerBootstrapper
{
    private static readonly string LOG_DIRECTORY = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DotNetTestRunner",
        "logs");

    private const string OUTPUT_TEMPLATE =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [User:{User}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Creates and installs the global Serilog logger. Safe to call once at application startup.
    /// </summary>
    /// <param name="settings">The log level and rolling file behavior to apply.</param>
    public static void Initialize(LoggingSettings settings)
    {
        Directory.CreateDirectory(LOG_DIRECTORY);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(ParseLogLevel(settings.MinimumLevel))
            .Enrich.WithProperty("User", Environment.UserName)
            .Enrich.WithProperty("Machine", Environment.MachineName)
            .WriteTo.File(
                Path.Combine(LOG_DIRECTORY, "testrunner-.log"),
                rollingInterval: ParseRollingInterval(settings.RollingInterval),
                retainedFileCountLimit: settings.RetainedFileCountLimit,
                fileSizeLimitBytes: settings.FileSizeLimitBytes,
                rollOnFileSizeLimit: settings.RollOnFileSizeLimit,
                shared: true,
                outputTemplate: OUTPUT_TEMPLATE)
            .CreateLogger();
    }

    /// <summary>
    /// Flushes and closes the global Serilog logger. Call during application shutdown so
    /// buffered entries are not lost.
    /// </summary>
    public static void Shutdown()
    {
        Log.CloseAndFlush();
    }

    private static LogEventLevel ParseLogLevel(string value)
    {
        return Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level)
            ? level
            : LogEventLevel.Information;
    }

    private static RollingInterval ParseRollingInterval(string value)
    {
        return Enum.TryParse<RollingInterval>(value, ignoreCase: true, out var interval)
            ? interval
            : RollingInterval.Day;
    }
}
