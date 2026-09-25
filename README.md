# .NET Test Runner

A Windows desktop application for discovering and running tests from a selected .NET solution (`.sln`) or project (`.csproj`). It provides a visual test tree, per-test status, configuration selection, live `dotnet test` output, and commands to run all, selected, class, or method-level tests.

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
publishes a self-contained Windows x64 package.

To create a downloadable package manually:

1. Open the repository's **Actions** tab.
2. Select **Release**.
3. Select **Run workflow** and choose the Windows architecture.
4. When the workflow finishes, download the versioned
   `DotNetTestRunner-v*-win-*` artifact from the workflow run.

The release workflow publishes a self-contained Windows application and stores
it as a ZIP artifact for 30 days. The target computer does not need a separate
.NET installation.

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

## Supported Configurations

The application project defines these configurations:

- `Debug`
- `Release`
- `MIQA`
- `UAT`

When testing another solution or project, the available configuration list is read from that target.

## Project Layout

- `MainWindow.xaml`: WPF user interface
- `ViewModels/MainWindowViewModel.cs`: test discovery, process execution, status, and output handling
- `Commands/`: synchronous and asynchronous command implementations
- `DotNetTestRunner.csproj`: application project

## Project History

See [CHANGELOG.md](CHANGELOG.md) for completed project milestones.
