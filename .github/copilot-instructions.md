# Project Guidelines

## Code Style
- Use PascalCase for types and public members, camelCase for parameters and local variables, `_PascalCase` for private/internal fields, and UPPER_CASE for constants.
- Prefix interfaces with `I`; do not use underscores in identifiers except for private/internal field prefixes and constants.
- Use built-in C# types (`int`, `string`, `bool`, `double`) rather than BCL aliases.
- Use Allman braces and separate methods or logical sections with one blank line.
- Wrap disposable resources in `using` declarations or statements.
- Declare local variables at the top of their scope and organize complex classes with `#region` sections.
- Add XML summaries to public methods and verify identifier spelling.
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

## Git And Commit Messages
- Use Conventional Commits with one focused concern per commit. Valid types include `feat`, `fix`, `chore`, `refactor`, `docs`, `style`, `test`, `perf`, `ci`, `build`, `revert`, and `security`.
- Write the subject in lowercase imperative mood, without a trailing period, and keep it under 50 characters. It must read naturally after: "If applied, this commit will ...".
- Use an optional body only when needed to explain why or impact; wrap it at 72 characters.
- When a JIRA ticket is known, use `type([JIRA-123], scope): subject`; do not invent ticket references.
- Do not add a `Co-authored-by: Copilot` trailer to commits.
- Stage deliberately and inspect `git diff --staged` before committing. Keep formatting-only changes in a separate `style` commit.
- Do not push, create pull requests, merge, or deploy unless explicitly requested.

## Documentation
- Keep README.md accurate when prerequisites, build/run commands, workflows, or project structure change.
- Record delivered user-facing, framework, dependency, or performance changes in CHANGELOG.md.
- Keep upcoming changes under `## Unreleased` in CHANGELOG.md.
- Before releasing a version, move its changes into a non-empty
  `## [<semantic-version>] - YYYY-MM-DD` section. The Release workflow uses the
  exact matching section as the GitHub Release description and fails if it is
  missing or empty.
- Document behavior and rationale, not implementation trivia; keep Markdown concise and scannable.

## Versioning And Releases
- The application version is the `<Version>` property in DotNetTestRunner.csproj.
- Use semantic versions such as `1.2.3` or `1.2.3-beta.1`. Version tags must use
  the corresponding `v<semantic-version>` form, such as `v1.2.3`.
- A version change pushed to `main` publishes a self-contained Windows x64 ZIP,
  creates or updates tag `v<Version>`, creates the GitHub Release, and attaches
  the ZIP as a release asset.
- Pushing a semantic-version tag also publishes the application using the tag
  version and creates or updates the matching GitHub Release and asset.
- Manual Release workflow runs may target x64, x86, or arm64 and produce an
  Actions artifact only; they do not create a formal GitHub Release.
- Keep release actions on Node.js 24-compatible major versions:
  `actions/checkout@v5`, `actions/setup-dotnet@v5`, and
  `actions/upload-artifact@v6`.
- Use the job-scoped `github.token` with `contents: write` for tags, releases,
  and release assets. Do not introduce a personal access token unless repository
  policy prevents the built-in token from performing the operation.

## Environment Notes
- This project is Windows-specific (WPF + net10.0-windows). Do not introduce cross-platform assumptions for UI execution.
- Test discovery and execution shell out to dotnet using the selected .sln or .csproj target path.
