# AGENTS.md

Project context for AI coding agents working in the **.NET Test Runner** (DotNetTestRunner)
repository.

## Project Overview

.NET Test Runner is a Windows desktop tool that discovers and runs tests from a selected
`.sln` or `.csproj` target. It shells out to the local `dotnet` executable to list and run
tests, parses `dotnet test` output into a class/method tree, and streams live output back to
the UI.

The application is a WPF (`WinExe`) app targeting `net10.0-windows`. There is no server,
database, or web API.

> Do not hardcode framework or package versions in generated content. Reference
> [DotNetTestRunner.csproj](DotNetTestRunner.csproj) as the source of truth.

## Repository Structure

This repository follows the same layered folder structure as the companion **DotNetPublisher**
application, so conventions are consistent across both codebases.

| Path | Purpose |
|------|---------|
| [App.xaml](App.xaml) / [.cs](App.xaml.cs) | Application entry point and merged resource dictionaries. |
| [MainWindow.xaml](MainWindow.xaml) / [.cs](MainWindow.xaml.cs) | Main application window. Code-behind is limited to UI wiring (font-size shortcut, `DataContext` setup). |
| `Application/Abstractions/` | Service interfaces (`ISettingsService`). |
| `Domain/Models/` | Serializable value models (`TestRunnerSettings`). |
| `Infrastructure/Services/` | Concrete service implementations (`SettingsService`). |
| `Presentation/ViewModels/` | MVVM view models (`MainWindowViewModel`). |
| `Presentation/Commands/` | `RelayCommand`, `AsyncRelayCommand`, `AsyncRelayCommand<T>` (hand-rolled `ICommand` implementations). |
| `Styles/` | Shared XAML resource dictionary (`Styles.xaml`) — see [Styles Convention](#styles-convention) below. |
| `bin/`, `obj/` | Build output (git-ignored). |

## Styles Convention

**All reusable, generic WPF styling lives in [`Styles/Styles.xaml`](Styles/Styles.xaml).**
This is the single common place for:

- Brushes (`SolidColorBrush` resources for the dark theme palette).
- Converters (`BooleanToVisibilityConverter`, etc.).
- Control styles that apply by `TargetType` (e.g. `TextBlock`, `TextBox`, `ComboBox`,
  `Button`, `TreeView`, `ContextMenu`, `MenuItem`) or by an explicit `x:Key` when they are
  reused across multiple templates (e.g. `DarkComboBoxItemStyle`, `DarkTreeViewItemStyle`).

`Styles.xaml` is merged once, at the `Application` level, in [App.xaml](App.xaml):

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Styles/Styles.xaml" />
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

Because it is merged at the application level, every window resolves these resources via
`StaticResource`/`DynamicResource` without needing to redeclare or re-merge them locally.

**What does *not* belong in `Styles.xaml`:** view-specific `DataTemplate`/
`HierarchicalDataTemplate` definitions that bind to a particular view model type (e.g. the
test tree templates in [MainWindow.xaml](MainWindow.xaml) bound to `vm:TestClassNode` /
`vm:TestMethodNode`). Those stay in the `.xaml` file of the view that owns them, in that
view's own `Grid.Resources` (or equivalent), because they are not reusable outside that
view's binding context.

When adding a new generic control style or brush:

1. Add it to [`Styles/Styles.xaml`](Styles/Styles.xaml), not to an individual window.
2. Reference it elsewhere with `StaticResource` (or `DynamicResource` if it is looked up
   before the resource is declared, as with `ItemContainerStyle`).
3. Do not duplicate brush/style declarations across windows — if two views need the same
   look, that is a signal it belongs in `Styles.xaml`.

## Tech Stack

- **Language:** C# with `Nullable` and `ImplicitUsings` enabled.
- **UI:** WPF (`UseWPF=true`), MVVM with hand-rolled `RelayCommand`/`AsyncRelayCommand` and
  `INotifyPropertyChanged`.
- **Target framework:** `net10.0-windows` (Windows-only; WPF cannot build or run on
  Linux/macOS).

See [DotNetTestRunner.csproj](DotNetTestRunner.csproj) for the authoritative framework and
package versions.

## Build & Run

Requires the .NET 10 SDK on **Windows** (WPF targets do not build on Linux/macOS runners).

```powershell
dotnet restore DotNetTestRunner.sln
dotnet build DotNetTestRunner.sln -c Debug
dotnet run --project DotNetTestRunner.csproj
```

Valid configurations for this project: `Debug`, `Release`, `MIQA`, `UAT`.

## Testing

There is no internal automated test project in this workspace. This app runs external tests
through `dotnet test` against whatever `.sln`/`.csproj` target the user selects. Validate
changes by building in `Debug`/`Release` and manually exercising the discovery/run workflow.

## Key Patterns and Conventions

- **Layered architecture:** interfaces live in `Application/Abstractions/`, implementations in
  `Infrastructure/Services/`, serializable models in `Domain/Models/`. View models depend only
  on interfaces (constructor injection — this app has no DI container, so
  [MainWindow.xaml.cs](MainWindow.xaml.cs) constructs and passes services directly).
- **Settings persistence:** [`MainWindowViewModel`](Presentation/ViewModels/MainWindowViewModel.cs)
  persists the selected target path and build configuration via `ISettingsService` to
  `%APPDATA%\DotNetTestRunner\settings.json`, restoring them at startup (falling back to the
  existing directory walk-up discovery when no valid persisted path exists). A guard flag
  (`_IsRestoringSettings`) prevents re-saving while values are being restored.
- **Naming conventions:** private/internal fields use an underscore + PascalCase prefix
  (`_TargetPath`, `_SettingsService`); constants use `UPPER_SNAKE_CASE`
  (`SETTINGS_FILE_PATH`); public types and members use PascalCase.
- **XML documentation** (`/// <summary>`) is present on public types and members — keep it
  that way.
- **`#region`/logical grouping:** organize large files into clearly separated sections
  (fields, construction, bindable properties, commands, private helpers).
- **External processes:** test discovery and execution run `dotnet test`/`dotnet build` via
  `MainWindowViewModel`, streaming stdout/stderr back to the UI through a dispatcher-marshaled
  output queue.

## Adding a New Service

Trace the full wiring chain — a new service is not usable until all steps are done:

1. Add the interface to `Application/Abstractions/I{Name}Service.cs`.
2. Add the implementation to `Infrastructure/Services/{Name}Service.cs`.
3. Add any serializable models it needs to `Domain/Models/`.
4. Construct it where it is consumed (e.g. [MainWindow.xaml.cs](MainWindow.xaml.cs)) and pass
   it into the consuming view model's constructor, storing it in a `readonly` field.

## Documentation

This `AGENTS.md` and the [README.md](README.md) are the primary references. Keep both in
sync when folder structure, styling conventions, or persisted settings behavior change.

## Common Pitfalls

- **Do not build on non-Windows runners** — WPF requires `windows-latest`.
- **Namespace collisions with `System.Windows.Application`:** because this project has an
  `Application/` folder (namespace `DotNetTestRunner.Application`), code that needs
  `System.Windows.Application` (e.g. `Application.Current`) must fully qualify it as
  `System.Windows.Application` to avoid `CS0118`/`CS0234` compiler errors.
- **Keep UI updates on the UI thread** — output streaming marshals back via the dispatcher.
- **Do not duplicate styles/brushes in individual windows** — add them to
  [`Styles/Styles.xaml`](Styles/Styles.xaml) instead (see
  [Styles Convention](#styles-convention)).
- **Do not commit `bin/` or `obj/`** — they are git-ignored.
