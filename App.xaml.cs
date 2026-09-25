using System.Configuration;
using System.Data;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DotNetTestRunner.Application.Abstractions;
using DotNetTestRunner.Infrastructure.Logging;
using DotNetTestRunner.Infrastructure.Services;

namespace DotNetTestRunner;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private ITestRunLogger? _TestRunLogger;

    /// <summary>
    /// Gets the shared application logger, available once <see cref="OnStartup"/> has run.
    /// </summary>
    internal ITestRunLogger? TestRunLogger => _TestRunLogger;

    /// <summary>
    /// Initializes logging and installs global exception handlers before the main window loads.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var loggingSettings = new LoggingSettingsProvider().Load();
        AppLoggerBootstrapper.Initialize(loggingSettings);

        _TestRunLogger = new TestRunLogger();
        _TestRunLogger.LogApplicationStarted();

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// Logs a normal shutdown and flushes any buffered log entries.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        _TestRunLogger?.LogApplicationStopped("Normal shutdown");
        AppLoggerBootstrapper.Shutdown();

        base.OnExit(e);
    }

    /// <summary>
    /// Logs unhandled exceptions raised on the UI dispatcher thread. The application still
    /// terminates afterward; the goal is to capture the cause before it is lost.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _TestRunLogger?.LogApplicationCrashed(e.Exception, "UI dispatcher");
        AppLoggerBootstrapper.Shutdown();
    }

    /// <summary>
    /// Logs unhandled exceptions raised outside the UI dispatcher (e.g. background threads).
    /// </summary>
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _TestRunLogger?.LogApplicationCrashed(exception, "AppDomain");
        }

        AppLoggerBootstrapper.Shutdown();
    }

    /// <summary>
    /// Logs exceptions from faulted tasks that were never observed, then marks them observed
    /// so they do not additionally crash the process via the finalizer thread.
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _TestRunLogger?.LogApplicationCrashed(e.Exception, "Task scheduler");
        e.SetObserved();
    }
}

