using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using DotNetTestRunner.Application.Abstractions;
using DotNetTestRunner.Domain.Models;

namespace DotNetTestRunner.Infrastructure.Services;

/// <summary>
/// Runs <c>dotnet</c> commands and streams their output back to the caller.
/// </summary>
public sealed class DotnetCommandRunner : IDotnetCommandRunner
{
    private const string DOTNET_EXECUTABLE = "dotnet";

    /// <summary>
    /// Exit code reported when the process was terminated before it could exit on its own.
    /// </summary>
    private const int TERMINATED_EXIT_CODE = -1;

    /// <summary>
    /// How long to keep draining a finished process's redirected output before abandoning it.
    /// A descendant process (such as an application launched by a UI test) inherits the
    /// output pipes, so the readers can stay open indefinitely after <c>dotnet</c> itself
    /// exits. Without this bound the run would never be observed as finished.
    /// </summary>
    private static readonly TimeSpan OUTPUT_DRAIN_GRACE_PERIOD = TimeSpan.FromSeconds(5);

    private readonly IProcessTreeInspector _ProcessTreeInspector;

    public DotnetCommandRunner(IProcessTreeInspector processTreeInspector)
    {
        _ProcessTreeInspector = processTreeInspector;
    }

    /// <inheritdoc />
    public async Task<DotnetCommandResult> RunAsync(
        DotnetCommandRequest request,
        Action<string> onOutput,
        CancellationTokenSource? runCancellation = null)
    {
        var cancellationToken = runCancellation?.Token ?? CancellationToken.None;

        using var process = new Process { StartInfo = CreateStartInfo(request) };
        process.Start();

        var drainTask = Task.WhenAll(
            ReadStreamAsync(process.StandardOutput, onOutput),
            ReadStreamAsync(process.StandardError, onOutput));

        using var exitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (request.Timeout.HasValue)
        {
            exitCancellation.CancelAfter(request.Timeout.Value);
        }

        var watchdog = new AppUnderTestWatchdog(_ProcessTreeInspector, onOutput);

        if (request.WatchForClosedAppUnderTest && runCancellation is not null)
        {
            watchdog.Start(process.Id, runCancellation);
        }

        try
        {
            await process.WaitForExitAsync(exitCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            await watchdog.StopAsync();
            await DrainOutputAsync(drainTask);

            var timedOut = !cancellationToken.IsCancellationRequested;

            onOutput(timedOut
                ? $"Command timed out after {request.Timeout!.Value.TotalSeconds:0} seconds."
                : "Run stopped. The dotnet process and any processes it started were terminated.");

            return new DotnetCommandResult(TERMINATED_EXIT_CODE, timedOut, watchdog.WasTriggered);
        }

        await watchdog.StopAsync();
        await DrainOutputAsync(drainTask);

        return new DotnetCommandResult(process.ExitCode, TimedOut: false, watchdog.WasTriggered);
    }

    private static ProcessStartInfo CreateStartInfo(DotnetCommandRequest request)
    {
        return new ProcessStartInfo
        {
            FileName = DOTNET_EXECUTABLE,
            Arguments = request.Arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.Exists(request.WorkingDirectory)
                ? request.WorkingDirectory
                : Environment.CurrentDirectory
        };
    }

    /// <summary>
    /// Waits a bounded time for the redirected output readers to reach end of stream.
    /// Processes started by the tests inherit the output pipes and can outlive <c>dotnet</c>
    /// itself, so the readers are abandoned once the grace period elapses rather than
    /// blocking the run forever.
    /// </summary>
    /// <param name="drainTask">The combined output reader task.</param>
    private static async Task DrainOutputAsync(Task drainTask)
    {
        var completedTask = await Task.WhenAny(drainTask, Task.Delay(OUTPUT_DRAIN_GRACE_PERIOD));

        if (ReferenceEquals(completedTask, drainTask))
        {
            await drainTask;
            return;
        }

        // The readers are abandoned but still pending. Observe any later fault so it cannot
        // surface through TaskScheduler.UnobservedTaskException and be logged as a crash.
        _ = drainTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Terminates a process and every process it started. Tests commonly launch applications
    /// as child processes, and those must not be left running after a stopped run.
    /// </summary>
    /// <param name="process">The process to terminate.</param>
    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            // The process already exited or cannot be terminated; stopping is best effort.
        }
    }

    private static async Task ReadStreamAsync(StreamReader reader, Action<string> onOutput)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                onOutput(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The stream was closed while this reader was abandoned after the grace period.
            // Any remaining output is unrecoverable and must not fault the run.
        }
    }
}
