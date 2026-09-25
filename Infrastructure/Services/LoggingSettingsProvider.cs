using System.IO;
using System.Text.Json;
using DotNetTestRunner.Application.Abstractions;
using DotNetTestRunner.Domain.Models;

namespace DotNetTestRunner.Infrastructure.Services;

/// <summary>
/// Reads <see cref="LoggingSettings"/> from <c>logsettings.json</c> deployed alongside the
/// application executable.
/// </summary>
public sealed class LoggingSettingsProvider : ILoggingSettingsProvider
{
    private static readonly string CONFIG_FILE_PATH = Path.Combine(AppContext.BaseDirectory, "logsettings.json");

    /// <inheritdoc />
    public LoggingSettings Load()
    {
        if (!File.Exists(CONFIG_FILE_PATH))
        {
            return new LoggingSettings();
        }

        try
        {
            var json = File.ReadAllText(CONFIG_FILE_PATH);
            var document = JsonSerializer.Deserialize<LoggingSettingsDocument>(json);
            return document?.Logging ?? new LoggingSettings();
        }
        catch
        {
            // Logging configuration is best-effort; a corrupt or unreadable file should not
            // prevent the application from starting with default logging settings.
            return new LoggingSettings();
        }
    }

    /// <summary>
    /// Root shape of <c>logsettings.json</c>, wrapping the settings under a "Logging" section.
    /// </summary>
    private sealed class LoggingSettingsDocument
    {
        public LoggingSettings? Logging { get; set; }
    }
}
