using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using DotNetTestRunner.Application.Abstractions;
using DotNetTestRunner.Domain.Models;
using DotNetTestRunner.Domain.Services;
using DotNetTestRunner.Presentation.Commands;
using DotNetTestRunner.Presentation.Reporting;
using Microsoft.Win32;

namespace DotNetTestRunner.Presentation.ViewModels;

/// <summary>
/// Drives the main window: choosing a target, discovering its tests, running them, and
/// reporting progress. Parsing, source scanning, and process execution are delegated to
/// services; this type owns only user-facing state and the order in which those services run.
/// </summary>
public sealed class MainWindowViewModel : ObservableObject
{
    #region Constants

    private const double MINIMUM_FONT_SIZE = 11;
    private const double MAXIMUM_FONT_SIZE = 26;
    private const double TREE_ICON_SIZE_OFFSET = 3;
    private const string PREFERRED_CONFIGURATION = "MIQA";

    /// <summary>
    /// Console logger settings that make <c>dotnet test</c> report every test result, not
    /// just the failures, so a run can be followed while it is still in progress.
    /// </summary>
    private const string RESULT_REPORTING_LOGGER = "--logger \"console;verbosity=normal\"";

    /// <summary>
    /// Configurations offered when a target does not declare its own.
    /// </summary>
    private static readonly string[] _DefaultConfigurations = ["Debug", "Release", "MIQA", "UAT"];

    /// <summary>
    /// How long test discovery may run before it is abandoned.
    /// </summary>
    private static readonly TimeSpan DISCOVERY_TIMEOUT = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How many trailing output lines to show when discovery fails.
    /// </summary>
    private const int FAILED_DISCOVERY_OUTPUT_LINE_COUNT = 30;

    #endregion

    #region Fields

    private readonly ISettingsService _SettingsService;
    private readonly TestRunReporter _TestRunReporter;
    private readonly IDotnetCommandRunner _DotnetCommandRunner;
    private readonly ITestDiscoveryService _TestDiscoveryService;
    private readonly ITestOutputParser _TestOutputParser;
    private readonly ITestTargetService _TestTargetService;
    private readonly OutputLogBuffer _OutputLogBuffer;
    private readonly Dispatcher _UiDispatcher;

    private string _TargetPath = string.Empty;
    private string _SelectedConfiguration = PREFERRED_CONFIGURATION;
    private string _StatusText = "Choose target .sln or .csproj";
    private bool _IsRunning;
    private double _UiFontSize = 14;
    private TestRunState _LastRunState = TestRunState.None;
    private bool _IsRestoringSettings;
    private string? _PendingRestoredConfiguration;
    private CancellationTokenSource? _RunCancellation;
    private RunStopReason _RunStopReason;

    #endregion

    #region Construction

    public MainWindowViewModel(
        ISettingsService settingsService,
        TestRunReporter testRunReporter,
        IDotnetCommandRunner dotnetCommandRunner,
        ITestDiscoveryService testDiscoveryService,
        ITestOutputParser testOutputParser,
        ITestTargetService testTargetService)
    {
        _SettingsService = settingsService;
        _TestRunReporter = testRunReporter;
        _DotnetCommandRunner = dotnetCommandRunner;
        _TestDiscoveryService = testDiscoveryService;
        _TestOutputParser = testOutputParser;
        _TestTargetService = testTargetService;

        var uiDispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _UiDispatcher = uiDispatcher;
        _OutputLogBuffer = new OutputLogBuffer(uiDispatcher);
        _OutputLogBuffer.PropertyChanged += OnOutputLogBufferChanged;

        TestTree = new TestTreeViewModel();
        TestTree.SelectionChanged += OnTestSelectionChanged;

        BrowseTargetCommand = new RelayCommand(BrowseTarget);
        RefreshTestsCommand = new AsyncRelayCommand(LoadTestsAsync, CanExecuteTestCommands);
        RunAllCommand = new AsyncRelayCommand(RunAllTestsAsync, CanExecuteTestCommands);
        RunSelectedCommand = new AsyncRelayCommand(RunSelectedTestsAsync, CanExecuteRunSelected);
        ClearSelectionCommand = new RelayCommand(ClearSelection, CanClearSelection);
        RunClassCommand = new AsyncRelayCommand<TestClassNode>(RunClassNodeAsync, CanExecuteRunClassNode);
        RunMethodCommand = new AsyncRelayCommand<TestMethodNode>(RunMethodNodeAsync, CanExecuteRunMethodNode);
        ClearLogCommand = new RelayCommand(_OutputLogBuffer.Clear);
        StopCommand = new RelayCommand(StopRun, CanStopRun);

        ResetConfigurations(_DefaultConfigurations);
        RestorePersistedTarget();
    }

    /// <summary>
    /// Restores the target used in the previous session, falling back to a target discovered
    /// near the application, and selects it without persisting the restored values again.
    /// </summary>
    private void RestorePersistedTarget()
    {
        var persistedSettings = _SettingsService.Load();
        var initialTarget = _TestTargetService.ResolveInitialTargetPath(persistedSettings.TargetPath);

        if (string.IsNullOrWhiteSpace(initialTarget))
        {
            return;
        }

        _IsRestoringSettings = true;
        _PendingRestoredConfiguration = persistedSettings.SelectedConfiguration;

        try
        {
            SetTargetPath(initialTarget);
        }
        finally
        {
            _IsRestoringSettings = false;
            _PendingRestoredConfiguration = null;
        }
    }

    #endregion

    #region Commands

    public RelayCommand BrowseTargetCommand { get; }

    public AsyncRelayCommand RefreshTestsCommand { get; }

    public AsyncRelayCommand RunAllCommand { get; }

    public AsyncRelayCommand RunSelectedCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public AsyncRelayCommand<TestClassNode> RunClassCommand { get; }

    public AsyncRelayCommand<TestMethodNode> RunMethodCommand { get; }

    public RelayCommand ClearLogCommand { get; }

    public RelayCommand StopCommand { get; }

    #endregion

    #region Bindable properties

    /// <summary>
    /// Gets the tree of discovered tests.
    /// </summary>
    public TestTreeViewModel TestTree { get; }

    /// <summary>
    /// Gets the test classes shown in the tree.
    /// </summary>
    public ObservableCollection<TestClassNode> TestClasses => TestTree.Classes;

    /// <summary>
    /// Gets the build configurations offered for the current target.
    /// </summary>
    public ObservableCollection<string> Configurations { get; } = [];

    /// <summary>
    /// Gets or sets the <c>.sln</c> or <c>.csproj</c> the runner targets.
    /// </summary>
    public string TargetPath
    {
        get => _TargetPath;
        set
        {
            if (SetProperty(ref _TargetPath, value))
            {
                NotifyCommandStateChanged();
                SavePersistedSettings();
            }
        }
    }

    /// <summary>
    /// Gets or sets the build configuration used for discovery and runs.
    /// </summary>
    public string SelectedConfiguration
    {
        get => _SelectedConfiguration;
        set
        {
            if (SetProperty(ref _SelectedConfiguration, value))
            {
                SavePersistedSettings();
            }
        }
    }

    /// <summary>
    /// Gets or sets the message shown beside the toolbar.
    /// </summary>
    public string StatusText
    {
        get => _StatusText;
        set => SetProperty(ref _StatusText, value);
    }

    /// <summary>
    /// Gets the accumulated console output of the current session.
    /// </summary>
    public string OutputLog => _OutputLogBuffer.Text;

    /// <summary>
    /// Gets or sets the font size of the window, clamped to a readable range.
    /// </summary>
    public double UiFontSize
    {
        get => _UiFontSize;
        set
        {
            if (SetProperty(ref _UiFontSize, Math.Clamp(value, MINIMUM_FONT_SIZE, MAXIMUM_FONT_SIZE)))
            {
                OnPropertyChanged(nameof(TreeIconSize));
            }
        }
    }

    /// <summary>
    /// Gets the size of the status icons in the test tree, scaled with the font.
    /// </summary>
    public double TreeIconSize => UiFontSize + TREE_ICON_SIZE_OFFSET;

    /// <summary>
    /// Gets or sets a value indicating whether a discovery or run is in progress.
    /// </summary>
    public bool IsRunning
    {
        get => _IsRunning;
        private set
        {
            if (SetProperty(ref _IsRunning, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the overall state of the most recent run.
    /// </summary>
    public TestRunState LastRunState
    {
        get => _LastRunState;
        private set
        {
            if (SetProperty(ref _LastRunState, value))
            {
                OnPropertiesChanged(nameof(IsRunInProgress), nameof(IsLastRunPassed), nameof(IsLastRunFailed));
            }
        }
    }

    public bool IsRunInProgress => LastRunState == TestRunState.Running;

    public bool IsLastRunPassed => LastRunState == TestRunState.Passed;

    public bool IsLastRunFailed => LastRunState == TestRunState.Failed;

    #endregion

    #region Public operations

    /// <summary>
    /// Changes the window font size by a relative amount.
    /// </summary>
    /// <param name="delta">Amount to add to the current size.</param>
    public void AdjustFontSize(double delta)
    {
        UiFontSize += delta;
    }

    #endregion

    #region Target selection

    private void BrowseTarget()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Test Targets (*.sln;*.csproj)|*.sln;*.csproj|Solution Files (*.sln)|*.sln|Project Files (*.csproj)|*.csproj",
            CheckFileExists = true,
            Multiselect = false,
            Title = "Select Solution or Project for dotnet test"
        };

        if (dialog.ShowDialog() == true)
        {
            SetTargetPath(dialog.FileName);
        }
    }

    /// <summary>
    /// Adopts a new target: reloads its configurations, clears the tree, and starts discovery.
    /// </summary>
    /// <param name="targetPath">Path to the <c>.sln</c> or <c>.csproj</c>.</param>
    private void SetTargetPath(string targetPath)
    {
        TargetPath = targetPath;

        var configurations = _TestTargetService.ReadConfigurations(targetPath);
        ResetConfigurations(configurations.Count > 0 ? configurations : _DefaultConfigurations);

        SelectedConfiguration = ChoosePreferredConfiguration();
        TestTree.Rebuild([]);

        AppendOutput($"Using test target: {targetPath}");
        StatusText = "Target selected. Discovering tests...";
        _ = LoadTestsAfterTargetSelectionAsync();
    }

    private void ResetConfigurations(IReadOnlyList<string> configurations)
    {
        Configurations.Clear();

        foreach (var configuration in configurations)
        {
            Configurations.Add(configuration);
        }
    }

    /// <summary>
    /// Picks the configuration to select for a newly adopted target, preferring the one
    /// restored from settings and otherwise the project's default.
    /// </summary>
    /// <returns>The configuration to select.</returns>
    private string ChoosePreferredConfiguration()
    {
        var restoredConfiguration = _PendingRestoredConfiguration is { Length: > 0 }
            ? FindConfiguration(_PendingRestoredConfiguration)
            : null;

        return restoredConfiguration
            ?? FindConfiguration(PREFERRED_CONFIGURATION)
            ?? Configurations[0];
    }

    private string? FindConfiguration(string name)
    {
        return Configurations.FirstOrDefault(
            configuration => string.Equals(configuration, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Persists the current target path and selected configuration so they can be restored
    /// the next time the application starts.
    /// </summary>
    private void SavePersistedSettings()
    {
        if (_IsRestoringSettings)
        {
            return;
        }

        _SettingsService.Save(new TestRunnerSettings
        {
            TargetPath = TargetPath,
            SelectedConfiguration = SelectedConfiguration
        });
    }

    #endregion

    #region Discovery

    private async Task LoadTestsAfterTargetSelectionAsync()
    {
        try
        {
            await LoadTestsAsync();
        }
        catch (Exception ex)
        {
            AppendOutput($"Failed to auto-discover tests: {ex.Message}");
            StatusText = "Failed to discover tests";
        }
    }

    private async Task LoadTestsAsync()
    {
        if (!CanExecuteTestCommands())
        {
            return;
        }

        IsRunning = true;

        using var discoveryCancellation = new CancellationTokenSource();
        _RunCancellation = discoveryCancellation;
        _RunStopReason = RunStopReason.None;
        StopCommand.NotifyCanExecuteChanged();

        try
        {
            StatusText = "Discovering tests...";
            AppendOutput($"{Environment.NewLine}--- Discover tests ({SelectedConfiguration}) ---");
            AppendOutput("Discovering tests. This can take a moment for large solutions.");

            var outputLines = new List<string>();
            var arguments = $"test \"{TargetPath}\" -c {SelectedConfiguration} --list-tests --nologo";

            AppendOutput($"> dotnet {arguments}");

            var result = await _DotnetCommandRunner.RunAsync(
                CreateRequest(arguments, DISCOVERY_TIMEOUT, watchForClosedAppUnderTest: false),
                line => outputLines.Add(line),
                discoveryCancellation);

            if (discoveryCancellation.IsCancellationRequested)
            {
                StatusText = "Test discovery stopped";
                AppendOutput(StatusText);
                return;
            }

            var discoveredTests = _TestOutputParser.ParseDiscoveredTestNames(outputLines);
            var resolvedEntries = await _TestDiscoveryService.ResolveEntriesAsync(discoveredTests, TargetPath);

            TestTree.Rebuild(resolvedEntries);
            NotifyCommandStateChanged();

            if (result.ExitCode != 0)
            {
                foreach (var line in outputLines.TakeLast(FAILED_DISCOVERY_OUTPUT_LINE_COUNT))
                {
                    AppendOutput(line);
                }
            }

            StatusText = result.ExitCode == 0
                ? $"Loaded {TestClasses.Count} classes and {resolvedEntries.Count} tests"
                : "Failed to discover tests";

            AppendOutput(StatusText);
        }
        finally
        {
            _RunCancellation = null;
            IsRunning = false;
        }
    }

    #endregion

    #region Running tests

    private async Task RunAllTestsAsync()
    {
        if (!CanExecuteTestCommands())
        {
            return;
        }

        var methods = TestTree.GetAllMethods();
        await ExecuteRunAsync(filter: null, "Running all tests", methods, TestClasses);
    }

    private async Task RunSelectedTestsAsync()
    {
        if (!CanExecuteTestCommands())
        {
            return;
        }

        var selectedMethods = TestTree.GetSelectedMethods();

        if (selectedMethods.Count == 0)
        {
            return;
        }

        var affectedClasses = TestTree.GetClassesWithSelectedMethods();
        var filter = TestFilterBuilder.ForExactTests(
            selectedMethods.Select(static methodNode => methodNode.FullyQualifiedName));

        var header = selectedMethods.Count == 1
            ? $"Running selected test {selectedMethods[0].FullyQualifiedName}"
            : $"Running {selectedMethods.Count} selected tests";

        await ExecuteRunAsync(filter, header, selectedMethods, affectedClasses);
    }

    private async Task RunClassNodeAsync(TestClassNode classNode)
    {
        if (!CanExecuteRunClassNode(classNode))
        {
            return;
        }

        var matchingClasses = TestTree.FindClasses(classNode.ClassName);
        var methods = matchingClasses.SelectMany(static node => node.Methods).ToList();

        if (methods.Count == 0)
        {
            return;
        }

        // A class filter must also match the generated names of data-driven cases, so the
        // tests are matched by prefix rather than exactly.
        var filter = TestFilterBuilder.ForTestsAndTheirCases(
            methods.Select(static methodNode => methodNode.FullyQualifiedName));

        await ExecuteRunAsync(filter, $"Running class {classNode.ClassName}", methods, matchingClasses);
    }

    private async Task RunMethodNodeAsync(TestMethodNode methodNode)
    {
        if (!CanExecuteRunMethodNode(methodNode))
        {
            return;
        }

        var fullyQualifiedName = methodNode.FullyQualifiedName;
        var matchingMethods = TestTree.FindMethods(fullyQualifiedName);
        var affectedClasses = TestTree.FindClassesContaining(fullyQualifiedName);
        var filter = TestFilterBuilder.ForExactTests([fullyQualifiedName]);

        await ExecuteRunAsync(filter, $"Running method {fullyQualifiedName}", matchingMethods, affectedClasses);
    }

    /// <summary>
    /// Runs a set of tests and reflects the outcome in the tree and the run log.
    /// </summary>
    /// <param name="filter">Test filter, or <see langword="null"/> to run every test.</param>
    /// <param name="header">Description of the run, used in status text and logs.</param>
    /// <param name="methods">The tests being run.</param>
    /// <param name="classes">The classes containing those tests.</param>
    private async Task ExecuteRunAsync(
        string? filter,
        string header,
        IReadOnlyList<TestMethodNode> methods,
        IReadOnlyList<TestClassNode> classes)
    {
        TestTreeViewModel.MarkRunning(methods, classes);

        var context = new TestRunContext(header, TargetPath, SelectedConfiguration);
        var progressTracker = new TestRunProgressTracker(
            _UiDispatcher,
            methods,
            classes,
            progress => OnTestRunProgress(context, progress));

        var capturedLines = new List<string>();
        var outcome = await RunTestsAsync(context, filter, capturedLines, progressTracker);

        // An interrupted run reports no per-test results, so its tests must be cleared rather
        // than marked failed.
        if (outcome.WasStopped)
        {
            TestTreeViewModel.ResetRunStates(methods, classes);
            return;
        }

        var failedNames = _TestOutputParser.ParseFailedTestNames(capturedLines);

        TestTreeViewModel.ApplyResults(methods, failedNames, outcome.ExitCode);
        TestTreeViewModel.UpdateClassStates(classes);
        _TestRunReporter.ReportSummary(context, methods, capturedLines);
    }

    /// <summary>
    /// Invokes <c>dotnet test</c> and translates the result into a run outcome, updating
    /// status text and writing the run log entries.
    /// </summary>
    /// <param name="context">Describes the run being carried out.</param>
    /// <param name="filter">Test filter, or <see langword="null"/> to run every test.</param>
    /// <param name="capturedLines">Receives every line of console output.</param>
    /// <param name="progressTracker">Follows the run's results as they are reported.</param>
    /// <returns>The outcome of the run.</returns>
    private async Task<TestRunOutcome> RunTestsAsync(
        TestRunContext context,
        string? filter,
        List<string> capturedLines,
        TestRunProgressTracker progressTracker)
    {
        IsRunning = true;
        LastRunState = TestRunState.Running;

        using var runCancellation = new CancellationTokenSource();
        _RunCancellation = runCancellation;
        _RunStopReason = RunStopReason.None;
        StopCommand.NotifyCanExecuteChanged();

        var stopwatch = Stopwatch.StartNew();
        _TestRunReporter.ReportStarted(context, filter);

        try
        {
            StatusText = "Running tests...";
            AppendOutput($"{Environment.NewLine}--- {context.Header} ({context.Configuration}) ---");

            // The console logger only names passing tests at normal verbosity, which is what
            // lets the tree and the log be updated test by test while the run is going.
            var arguments =
                $"test \"{context.TargetPath}\" -c {context.Configuration} --nologo {RESULT_REPORTING_LOGGER}";

            if (!string.IsNullOrWhiteSpace(filter))
            {
                arguments += $" --filter \"{filter}\"";
            }

            AppendOutput($"> dotnet {arguments}");

            var result = await _DotnetCommandRunner.RunAsync(
                CreateRequest(arguments, timeout: null, watchForClosedAppUnderTest: true),
                line => CaptureOutput(capturedLines, line, progressTracker),
                runCancellation);

            if (runCancellation.IsCancellationRequested || result.TimedOut)
            {
                return ReportStoppedRun(context, result, stopwatch.Elapsed);
            }

            StatusText = result.ExitCode == 0 ? "Test run completed" : "Test run failed";
            LastRunState = result.ExitCode == 0 ? TestRunState.Passed : TestRunState.Failed;
            _TestRunReporter.ReportCompleted(context, result.ExitCode, stopwatch.Elapsed);

            return new TestRunOutcome(result.ExitCode, WasStopped: false);
        }
        catch (Exception ex)
        {
            AppendOutput($"Test run failed unexpectedly: {ex.Message}");
            StatusText = "Test run failed";
            LastRunState = TestRunState.Failed;
            _TestRunReporter.ReportError(context, ex);

            return new TestRunOutcome(-1, WasStopped: false);
        }
        finally
        {
            _RunCancellation = null;
            IsRunning = false;
        }
    }

    /// <summary>
    /// Records a run that ended before all of its tests reported, distinguishing a run the
    /// user stopped from one abandoned because the application under test was closed.
    /// </summary>
    /// <param name="context">Describes the run that was stopped.</param>
    /// <param name="result">Result reported by the command runner.</param>
    /// <param name="duration">How long the run lasted.</param>
    /// <returns>A stopped outcome.</returns>
    private TestRunOutcome ReportStoppedRun(
        TestRunContext context,
        DotnetCommandResult result,
        TimeSpan duration)
    {
        var stopReason = ResolveStopReason(result);

        StatusText = stopReason switch
        {
            RunStopReason.ApplicationClosed => "Test run stopped: application closed",
            RunStopReason.TimedOut => "Test run timed out",
            _ => "Test run stopped"
        };

        LastRunState = TestRunState.None;
        _TestRunReporter.ReportStopped(context, stopReason, duration);

        return new TestRunOutcome(result.ExitCode, WasStopped: true);
    }

    /// <summary>
    /// Determines why a run ended early, preferring the reason observed by the command runner
    /// over the one recorded when the user pressed Stop.
    /// </summary>
    /// <param name="result">Result reported by the command runner.</param>
    /// <returns>The reason the run stopped.</returns>
    private RunStopReason ResolveStopReason(DotnetCommandResult result)
    {
        if (result.WasAppUnderTestClosed)
        {
            return RunStopReason.ApplicationClosed;
        }

        if (result.TimedOut)
        {
            return RunStopReason.TimedOut;
        }

        return _RunStopReason == RunStopReason.None ? RunStopReason.User : _RunStopReason;
    }

    private DotnetCommandRequest CreateRequest(string arguments, TimeSpan? timeout, bool watchForClosedAppUnderTest)
    {
        return new DotnetCommandRequest
        {
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(TargetPath) ?? Environment.CurrentDirectory,
            Timeout = timeout,
            WatchForClosedAppUnderTest = watchForClosedAppUnderTest
        };
    }

    private void CaptureOutput(List<string> capturedLines, string line, TestRunProgressTracker progressTracker)
    {
        lock (capturedLines)
        {
            capturedLines.Add(line);
        }

        AppendOutput(line);

        var notice = _TestOutputParser.ReadResultNotice(line);

        if (notice is not null)
        {
            progressTracker.Report(notice);
        }
    }

    /// <summary>
    /// Shows and logs a test result reported while the run is still going.
    /// </summary>
    /// <param name="context">The run in progress.</param>
    /// <param name="progress">The reported test, its result, and how far the run has got.</param>
    private void OnTestRunProgress(TestRunContext context, TestRunProgress progress)
    {
        // A stopped run reports no further results, so its status text must not be overwritten.
        if (_RunCancellation is { IsCancellationRequested: false })
        {
            StatusText = $"Running tests... {progress.CompletedCount}/{progress.TotalCount} done";
        }

        _TestRunReporter.ReportProgress(context, progress);
    }

    #endregion

    #region Stopping

    /// <summary>
    /// Requests cancellation of the in-progress run. The <c>dotnet</c> process and every
    /// process it started are terminated so a hung or abandoned test cannot keep the runner
    /// stuck in the running state.
    /// </summary>
    private void StopRun()
    {
        var runCancellation = _RunCancellation;

        if (runCancellation is null || runCancellation.IsCancellationRequested)
        {
            return;
        }

        StatusText = "Stopping test run...";
        AppendOutput("Stopping test run...");
        _RunStopReason = RunStopReason.User;
        runCancellation.Cancel();
        StopCommand.NotifyCanExecuteChanged();
    }

    private bool CanStopRun()
    {
        return IsRunning && _RunCancellation is { IsCancellationRequested: false };
    }

    #endregion

    #region Selection

    private void ClearSelection()
    {
        TestTree.ClearSelection();
    }

    private bool CanClearSelection()
    {
        return TestTree.HasSelectedMethods();
    }

    private void OnTestSelectionChanged(object? sender, EventArgs e)
    {
        RunSelectedCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region Command availability

    private bool CanExecuteTestCommands()
    {
        return !IsRunning
            && !string.IsNullOrWhiteSpace(TargetPath)
            && File.Exists(TargetPath);
    }

    private bool CanExecuteRunSelected()
    {
        return CanExecuteTestCommands() && TestTree.HasSelectedMethods();
    }

    private bool CanExecuteRunClassNode(TestClassNode classNode)
    {
        return CanExecuteTestCommands() && !string.IsNullOrWhiteSpace(classNode.ClassName);
    }

    private bool CanExecuteRunMethodNode(TestMethodNode methodNode)
    {
        return CanExecuteTestCommands() && !string.IsNullOrWhiteSpace(methodNode.FullyQualifiedName);
    }

    private void NotifyCommandStateChanged()
    {
        RefreshTestsCommand.NotifyCanExecuteChanged();
        RunAllCommand.NotifyCanExecuteChanged();
        RunSelectedCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        RunClassCommand.NotifyCanExecuteChanged();
        RunMethodCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region Output

    private void AppendOutput(string line)
    {
        _OutputLogBuffer.Append(line);
    }

    private void OnOutputLogBufferChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OutputLogBuffer.Text))
        {
            OnPropertyChanged(nameof(OutputLog));
        }
    }

    #endregion
}
