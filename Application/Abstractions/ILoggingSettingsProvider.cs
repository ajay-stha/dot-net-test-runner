namespace DotNetTestRunner.Application.Abstractions;

using DotNetTestRunner.Domain.Models;

/// <summary>
/// Loads <see cref="LoggingSettings"/> used to configure log verbosity and rolling file behavior.
/// </summary>
public interface ILoggingSettingsProvider
{
    /// <summary>
    /// Loads the persisted logging configuration, or default values when none exists or the
    /// configuration file cannot be read.
    /// </summary>
    LoggingSettings Load();
}
