using DotNetTestRunner.Domain.Models;

namespace DotNetTestRunner.Application.Abstractions;

/// <summary>
/// Persists and restores user-selected test runner settings across sessions.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Loads previously persisted settings, or default values when none exist or the
    /// persisted file cannot be read.
    /// </summary>
    TestRunnerSettings Load();

    /// <summary>
    /// Persists the supplied settings for the next application session.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    void Save(TestRunnerSettings settings);
}
