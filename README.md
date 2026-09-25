# .NET Test Runner

A Windows desktop application for discovering and running tests from a selected .NET solution (`.sln`) or project (`.csproj`). It provides a visual test tree, per-test status, configuration selection, live `dotnet test` output, and commands to run all, selected, class, or method-level tests. It uses a custom borderless title bar matching the companion DotNetPublisher application's design.

## Requirements

- Windows
- .NET 10 SDK or later
- A test target supported by `dotnet test`

## Run Locally

```powershell
dotnet restore DotNetTestRunner.sln
dotnet run --project DotNetTestRunner.csproj
```

To create a release build:

```powershell
dotnet build DotNetTestRunner.sln -c Release
```

## GitHub Actions

The **Build** workflow restores and compiles the solution in the `Release`
configuration for pushes to `main`, pull requests, and manual runs.

The application version is defined by the `<Version>` property in
`DotNetTestRunner.csproj`. Use semantic versions such as `1.0.0` or `1.1.0`.
When that value changes on `main`, the **Release** workflow automatically
publishes a self-contained Windows x64 package, creates the corresponding
`v<Version>` tag and GitHub Release, and attaches the ZIP to the release.

Creating a semantic-version tag such as `v1.2.3` also runs the workflow. For a
tagged release, the version from the tag is applied to the published application
and artifact, even if the project file contains a different version. The ZIP is
attached to the GitHub Release for that tag.

To create a downloadable package manually:

1. Open the repository's **Actions** tab.
2. Select **Release**.
3. Select **Run workflow** and choose the Windows architecture.
4. When the workflow finishes, download the versioned
   `DotNetTestRunner-v*-win-*` artifact from the workflow run.

The release workflow publishes a self-contained Windows application. Workflow
artifacts are retained for 30 days; assets attached to GitHub Releases remain
available with their releases. The target computer does not need a separate
.NET installation.

Each released version must have a matching changelog heading in the form
`## [1.2.3] - YYYY-MM-DD`. The workflow uses the content under that heading as
the GitHub Release description and fails explicitly when the section is missing
or empty. Keep upcoming changes under `## Unreleased`, then move them into a
versioned section when updating the application version or creating a tag.

## Use the Application

1. Select a test solution or project with **Browse**.
2. Choose a build configuration. The application reads configurations from the selected target and defaults to `MIQA` when it is available.
3. Wait for automatic discovery or select **Refresh Tests**.
4. Run the tests you need:
   - **Run All** runs every discovered test.
   - Select individual tests, then use **Run Selected**.
   - Right-click a class or test method to run it directly.
5. Review pass/fail status in the test tree and command output in the log panel.

The runner executes standard `dotnet test` commands with the selected configuration. Selected tests use `FullyQualifiedName` filters, so their test adapters must support the standard VSTest filter syntax.

The last selected target path and build configuration are persisted automatically (see
[Persisted Settings](#persisted-settings)) and restored the next time the application starts.

## Supported Configurations

The application project defines these configurations:

- `Debug`
- `Release`
- `MIQA`
- `UAT`

When testing another solution or project, the available configuration list is read from that target.

## Persisted Settings

The application remembers the last selected target path and build configuration between
sessions. Settings are stored as JSON at:

```
%APPDATA%\DotNetTestRunner\settings.json
```

On startup, the application restores the persisted target path if the file still exists on
disk; otherwise it falls back to searching parent directories of the executable for
`CSM.UI.Tests.sln`/`CSM.UI.Tests.csproj`. Settings are saved automatically whenever the
target path or selected configuration changes — no manual save action is required. See
[`ISettingsService`](Application/Abstractions/ISettingsService.cs) and its implementation,
[`SettingsService`](Infrastructure/Services/SettingsService.cs), for details.

## Logging

The application logs to a rolling daily file at:

```
%APPDATA%\DotNetTestRunner\logs\testrunner-<date>.log
```

Each entry records a timestamp, log level, and the Windows user account that produced it, for
example:

```
[2026-09-25 14:10:49.723] [INF] [User:jdoe] Application started.
[2026-09-25 14:11:02.104] [INF] [User:jdoe] Test run started. Run: Running all tests, Target: C:\src\App.sln, Configuration: MIQA, Filter: (none)
[2026-09-25 14:11:18.552] [WRN] [User:jdoe] Test run summary for Running all tests: 11 passed, 1 failed.
[2026-09-25 14:11:18.553] [ERR] [User:jdoe] Test failed. Run: Running all tests, Test: MyApp.Tests.FooTests.Bar_ShouldReturnTrue, Cause: Assert.That(1 + 1, Is.EqualTo(3)) Expected: 3 But was: 2
```

What gets logged:

- Application started and stopped (normal shutdown).
- Unhandled/crash conditions from the UI thread, background threads, and unobserved task
  exceptions, so an unexpected stop leaves a record of what caused it.
- Each test run's start (target, configuration, filter), completion (exit code, duration),
  and a pass/fail summary.
- For each failed test, its own log entry naming the test and the failure cause (the "Error
  Message:" text reported by the test framework), when it can be parsed from the `dotnet
  test` console output.

Log verbosity and rolling behavior are configured in `logsettings.json`, deployed next to the
application executable:

```json
{
  "Logging": {
    "MinimumLevel": "Information",
    "RollingInterval": "Day",
    "RetainedFileCountLimit": 31,
    "FileSizeLimitBytes": 10485760,
    "RollOnFileSizeLimit": true
  }
}
```

- `MinimumLevel`: `Verbose`, `Debug`, `Information`, `Warning`, `Error`, or `Fatal`.
- `RollingInterval`: `Infinite`, `Year`, `Month`, `Day`, `Hour`, or `Minute`.
- `RetainedFileCountLimit`: number of rolled files kept (older files are deleted), or `null`
  to keep them all.
- `FileSizeLimitBytes`: size at which a file rolls over regardless of the interval, or `null`
  to disable.
- `RollOnFileSizeLimit`: whether `FileSizeLimitBytes` rolling is enabled.

See [`ILoggingSettingsProvider`](Application/Abstractions/ILoggingSettingsProvider.cs),
[`ITestRunLogger`](Application/Abstractions/ITestRunLogger.cs), and their implementations in
[`Infrastructure/Logging`](Infrastructure/Logging) for details.

## Project Layout

This project follows a layered architecture. See [AGENTS.md](AGENTS.md) for the full
folder-structure and styling convention reference.

- `MainWindow.xaml` / `.cs`: WPF main window (custom title bar/header, UI layout, minimal code-behind).
- `Application/Abstractions/`: service interfaces (`ISettingsService`, `ILoggingSettingsProvider`, `ITestRunLogger`).
- `Domain/Models/`: serializable value models (`TestRunnerSettings`, `LoggingSettings`).
- `Infrastructure/Services/`: concrete service implementations (`SettingsService`, `LoggingSettingsProvider`).
- `Infrastructure/Logging/`: Serilog bootstrap and `ITestRunLogger` implementation (`AppLoggerBootstrapper`, `TestRunLogger`).
- `Presentation/ViewModels/`: MVVM view models (`MainWindowViewModel`).
- `Presentation/Commands/`: reusable `ICommand` implementations (`RelayCommand`, `AsyncRelayCommand`, `AsyncRelayCommand<T>`).
- `Presentation/Behaviors/`: reusable XAML attached behaviors (`OutputLogBehavior` colorizes the execution log).
- `Styles/Styles.xaml`: shared brushes, converters, and control styles, merged at the application level.
- `logsettings.json`: log level and rolling file configuration, deployed alongside the executable.
- `DotNetTestRunner.csproj`: application project.

## Header / Title Bar

The application replaces the standard Windows title bar with a custom header (app icon,
accent bar, title/subtitle, and minimize/maximize/close buttons), matching the companion
DotNetPublisher application's design. The window remains fully resizable and drag-movable via
the header, native rounded corners are applied where supported, and maximizing never covers
the taskbar. See the [Header / Title Bar Convention](AGENTS.md#header--title-bar-convention)
section of [AGENTS.md](AGENTS.md) for implementation details.

## Project History

See [CHANGELOG.md](CHANGELOG.md) for completed project milestones.
