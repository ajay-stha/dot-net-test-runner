using System.IO;
using System.Text.Json;
using DotNetTestRunner.Application.Abstractions;
using DotNetTestRunner.Domain.Models;

namespace DotNetTestRunner.Infrastructure.Services;

/// <summary>
/// Persists <see cref="TestRunnerSettings"/> as JSON under the current user's local
/// application data folder.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly string SETTINGS_FILE_PATH = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DotNetTestRunner",
        "settings.json");

    /// <inheritdoc />
    public TestRunnerSettings Load()
    {
        if (!File.Exists(SETTINGS_FILE_PATH))
        {
            return new TestRunnerSettings();
        }

        try
        {
            var json = File.ReadAllText(SETTINGS_FILE_PATH);
            return JsonSerializer.Deserialize<TestRunnerSettings>(json) ?? new TestRunnerSettings();
        }
        catch
        {
            // Settings persistence is best-effort; a corrupt or unreadable file should not
            // prevent the application from starting with default settings.
            return new TestRunnerSettings();
        }
    }

    /// <inheritdoc />
    public void Save(TestRunnerSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(SETTINGS_FILE_PATH);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(SETTINGS_FILE_PATH, json);
        }
        catch
        {
            // Settings persistence is best-effort; failures should not interrupt the user's workflow.
        }
    }
}
