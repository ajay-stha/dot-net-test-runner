using DotNetTestRunner.Application.Abstractions;

namespace DotNetTestRunner.Infrastructure.Services;

/// <summary>
/// Watches the applications a test run starts and cancels the run once they have all been
/// closed. UI tests drive an application launched as a descendant of <c>dotnet test</c>; if
/// that application is closed, the test framework keeps polling for windows that will never
/// appear and the run would otherwise hang until it is stopped by hand.
/// </summary>
internal sealed class AppUnderTestWatchdog
{
    /// <summary>
    /// How long every application started by a run must stay closed before the run is treated
    /// as abandoned. UI tests close the application between test cases, so a short absence is
    /// normal and must not end the run.
    /// </summary>
    private static readonly TimeSpan ABSENCE_GRACE_PERIOD = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often the process tree is polled while a run is in progress.
    /// </summary>
    private static readonly TimeSpan POLL_INTERVAL = TimeSpan.FromSeconds(2);

    private readonly IProcessTreeInspector _ProcessTreeInspector;
    private readonly Action<string> _OnOutput;
    private readonly CancellationTokenSource _WatchdogCancellation = new();
    private Task _WatchTask = Task.CompletedTask;

    public AppUnderTestWatchdog(IProcessTreeInspector processTreeInspector, Action<string> onOutput)
    {
        _ProcessTreeInspector = processTreeInspector;
        _OnOutput = onOutput;
    }

    /// <summary>
    /// Gets a value indicating whether the watchdog stopped the run because the application
    /// under test had been closed.
    /// </summary>
    public bool WasTriggered { get; private set; }

    /// <summary>
    /// Begins watching the descendants of a process.
    /// </summary>
    /// <param name="rootProcessId">The <c>dotnet</c> process whose descendants are watched.</param>
    /// <param name="runCancellation">Cancelled when the application under test disappears.</param>
    public void Start(int rootProcessId, CancellationTokenSource runCancellation)
    {
        _WatchTask = WatchAsync(rootProcessId, runCancellation);
    }

    /// <summary>
    /// Stops watching and waits for the watch loop to finish, so it cannot outlive the run.
    /// </summary>
    public async Task StopAsync()
    {
        await _WatchdogCancellation.CancelAsync();

        try
        {
            await _WatchTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when the watch loop is cancelled.
        }
        finally
        {
            _WatchdogCancellation.Dispose();
        }
    }

    private async Task WatchAsync(int rootProcessId, CancellationTokenSource runCancellation)
    {
        var watchdogToken = _WatchdogCancellation.Token;
        var hasSeenAppUnderTest = false;
        DateTime? absentSince = null;

        try
        {
            while (!watchdogToken.IsCancellationRequested && !runCancellation.IsCancellationRequested)
            {
                await Task.Delay(POLL_INTERVAL, watchdogToken);

                if (_ProcessTreeInspector.CountDescendantsWithVisibleWindow(rootProcessId) > 0)
                {
                    hasSeenAppUnderTest = true;
                    absentSince = null;
                    continue;
                }

                // Nothing is shown yet during build and test discovery, so the run is only
                // considered abandoned once an application has actually been seen. This also
                // keeps the watchdog inert for test suites that never open a window.
                if (!hasSeenAppUnderTest)
                {
                    continue;
                }

                absentSince ??= DateTime.UtcNow;

                if (DateTime.UtcNow - absentSince.Value < ABSENCE_GRACE_PERIOD)
                {
                    continue;
                }

                _OnOutput(
                    "The application under test is no longer running. Stopping the run after "
                    + $"{ABSENCE_GRACE_PERIOD.TotalSeconds:0} seconds without it.");

                WasTriggered = true;
                await runCancellation.CancelAsync();
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // The run finished or was stopped; the watchdog is no longer needed.
        }
    }
}
