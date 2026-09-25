# Changelog

All notable changes to this project are documented in this file.

## Unreleased

### Added

- Added structured logging via Serilog: application start/stop events, unhandled/crash
  conditions (UI dispatcher, `AppDomain`, and unobserved task exceptions), and per-test-run
  activity (start, completion, and pass/fail summaries with failed test names and the
  Windows user account that ran them) are written to a rolling daily log file under
  `%APPDATA%\DotNetTestRunner\logs\`.
- Added `logsettings.json` (deployed alongside the executable) to configure the minimum log
  level and rolling file behavior (rolling interval, retained file count, and file size
  limit) without recompiling. See `ILoggingSettingsProvider`/`LoggingSettingsProvider` and
  `AppLoggerBootstrapper`.
- Added `ITestRunLogger`/`TestRunLogger` following the existing layered architecture
  (`Application/Abstractions`, `Infrastructure/Logging`, `Domain/Models`).

## [1.0.1] - 2026-09-25

### Added

- Added persisted settings: the last selected target path and build configuration are now
  saved to `%APPDATA%\DotNetTestRunner\settings.json` and restored automatically on startup,
  falling back to the existing directory walk-up discovery when no valid persisted path
  exists.
- Added `ISettingsService`/`SettingsService` following the layered architecture used by the
  companion DotNetPublisher application (`Application/Abstractions`, `Infrastructure/Services`,
  `Domain/Models`).
- Added [AGENTS.md](AGENTS.md) documenting the repository's folder structure and the shared
  styles convention.
- Added a custom borderless title bar/header matching the companion DotNetPublisher
  application's design: app icon, accent bar, title/subtitle, and minimize/maximize/close
  caption buttons, with native rounded corners and monitor-aware maximize sizing so the
  maximized window never covers the taskbar.
- Added GitHub Release creation for version changes and semantic-version tags, with the
  published Windows ZIP attached as a release asset.
- Added release descriptions sourced from the matching version section in this changelog.

### Changed

- Restructured the project into a layered folder structure matching DotNetPublisher:
  `ViewModels/` and `Commands/` moved under `Presentation/`; new `Application/Abstractions/`,
  `Infrastructure/Services/`, and `Domain/Models/` folders were added.
- Extracted all shared brushes, converters, and generic control styles from
  `MainWindow.xaml` into a common `Styles/Styles.xaml` resource dictionary, merged once at
  the `Application` level. View-specific data templates remain in `MainWindow.xaml`.

## [1.0.0] - 2026-09-25

### Added

- Initialized the Git repository with generated build and editor artifacts ignored.
- Renamed the application to `.NET Test Runner`, including the solution, project metadata, application title, and namespaces.
- Added a project README with prerequisites, build commands, and test-runner usage.
- Added GitHub Actions workflows for build validation and manual, self-contained Windows release artifacts.
- Added semantic application versioning with automatic release publishing when the version changes on `main`.
- Added tag-triggered releases that apply `v<semantic-version>` tags to published application metadata.

### Changed

- Upgraded the WPF application target from `net8.0-windows` to `net10.0-windows`.
- Updated `Microsoft.CodeAnalysis.CSharp` from `4.11.0` to `5.9.0`.
- Updated GitHub Actions to Node.js 24-compatible action versions and made
  release version checks run on every push to `main`.

### Performance

- Replaced the static runtime-compiled test-name regex with a source-generated regex.
