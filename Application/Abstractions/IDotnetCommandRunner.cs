using DotNetTestRunner.Domain.Models;

namespace DotNetTestRunner.Application.Abstractions;

/// <summary>
/// Runs <c>dotnet</c> commands, streaming their console output back to the caller and
/// guaranteeing that the process and everything it started are cleaned up.
/// </summary>
public interface IDotnetCommandRunner
{
    /// <summary>
    /// Runs a <c>dotnet</c> command to completion.
    /// </summary>
    /// <param name="request">The command to run and how to supervise it.</param>
    /// <param name="onOutput">
    /// Receives each line of standard output and standard error. May be called from a
    /// background thread.
    /// </param>
    /// <param name="runCancellation">
    /// Cancelled to stop the command. The runner also cancels this source itself when the
    /// application under test is detected as closed, so callers can observe why the run ended.
    /// </param>
    /// <returns>The exit code and why the command stopped, if it stopped early.</returns>
    Task<DotnetCommandResult> RunAsync(
        DotnetCommandRequest request,
        Action<string> onOutput,
        CancellationTokenSource? runCancellation = null);
}
