namespace DotNetTestRunner.Domain.Models;

/// <summary>
/// Outcome of a test run. <see cref="WasStopped"/> distinguishes a run that was cancelled
/// from one that genuinely failed, so cancelled runs do not mark tests as failed.
/// </summary>
/// <param name="ExitCode">Exit code reported by <c>dotnet test</c>.</param>
/// <param name="WasStopped">Whether the run ended before all tests had reported.</param>
public readonly record struct TestRunOutcome(int ExitCode, bool WasStopped)
{
    /// <summary>
    /// Gets a value indicating whether every test in the run passed.
    /// </summary>
    public bool IsSuccess => !WasStopped && ExitCode == 0;
}
