# Changelog

All notable changes to this project are documented in this file.

## 2026-09-25

### Added

- Initialized the Git repository with generated build and editor artifacts ignored.
- Renamed the application to `.NET Test Runner`, including the solution, project metadata, application title, and namespaces.
- Added a project README with prerequisites, build commands, and test-runner usage.
- Added GitHub Actions workflows for build validation and manual, self-contained Windows release artifacts.
- Added semantic application versioning with automatic release publishing when the version changes on `main`.

### Changed

- Upgraded the WPF application target from `net8.0-windows` to `net10.0-windows`.
- Updated `Microsoft.CodeAnalysis.CSharp` from `4.11.0` to `5.9.0`.

### Performance

- Replaced the static runtime-compiled test-name regex with a source-generated regex.
