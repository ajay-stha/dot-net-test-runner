# Project Guidelines

## Code Style
- Use C# naming conventions used in this repository:
  - Public members and types: PascalCase
  - Private fields: _PascalCase
  - Constants: SNAKE_CASE
- Keep nullable reference types enabled and avoid suppressing warnings unless required.
- Prefer concise, focused methods in ViewModels and Commands.

## Architecture
- This is a WPF MVVM desktop app targeting net10.0-windows.
- Keep responsibilities separated:
  - Views (.xaml): layout and bindings only.
  - Code-behind (.xaml.cs): minimal UI wiring only (for example selection forwarding).
  - ViewModels: state, command orchestration, and process execution.
  - Commands: reusable ICommand implementations (sync and async).
- Do not move test execution logic into code-behind.

## Build And Test
- Restore:
  - dotnet restore DotNetTestRunner.sln
  - dotnet restore DotNetTestRunner.csproj
- Build:
  - dotnet build DotNetTestRunner.sln -c Debug
  - dotnet build DotNetTestRunner.csproj -c Release
- Run app:
  - dotnet run --project DotNetTestRunner.csproj
- Valid configurations in this project: Debug, Release, MIQA, UAT.
- There is no internal unit test project in this workspace. This app runs external tests through dotnet test.

## Conventions
- Use AsyncRelayCommand for long-running operations and RelayCommand for quick synchronous actions.
- Keep command can-execute state in sync by raising NotifyCanExecuteChanged when relevant state changes.
- Preserve UI-thread safety when updating bound properties from process output (Dispatcher usage).
- When editing, ignore generated artifacts under obj/ and bin/ unless explicitly required.

## Environment Notes
- This project is Windows-specific (WPF + net10.0-windows). Do not introduce cross-platform assumptions for UI execution.
- Test discovery and execution shell out to dotnet using the selected .sln or .csproj target path.
