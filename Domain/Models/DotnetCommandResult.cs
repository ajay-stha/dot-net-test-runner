namespace DotNetTestRunner.Domain.Models;

/// <summary>
/// The outcome of a <c>dotnet</c> invocation.
/// </summary>
/// <param name="ExitCode">
/// Exit code reported by the process, or <c>-1</c> when it was terminated before exiting.
/// </param>
/// <param name="TimedOut">Whether the command exceeded its allotted time.</param>
/// <param name="WasAppUnderTestClosed">
/// Whether the command was stopped because every application it started had been closed.
/// </param>
public readonly record struct DotnetCommandResult(int ExitCode, bool TimedOut, bool WasAppUnderTestClosed);
