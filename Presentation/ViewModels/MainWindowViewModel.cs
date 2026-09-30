using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Xml.Linq;
using DotNetTestRunner.Application.Abstractions;
using DotNetTestRunner.Domain.Models;
using DotNetTestRunner.Presentation.Commands;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Win32;

namespace DotNetTestRunner.Presentation.ViewModels;

public sealed partial class MainWindowViewModel : INotifyPropertyChanged
{
    [GeneratedRegex(
        "^[A-Za-z_][A-Za-z0-9_+.()]*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TestNameRegex();

    private static readonly string[] _DefaultConfigurations =
    [
        "Debug",
        "Release",
        "MIQA",
        "UAT"
    ];

    private readonly ISettingsService _SettingsService;
    private readonly ITestRunLogger _TestRunLogger;
    private readonly IProcessTreeInspector _ProcessTreeInspector;
    private string _TargetPath = string.Empty;
    private string _SelectedConfiguration = "MIQA";
    private string _StatusText = "Choose target .sln or .csproj";
    private string _OutputLog = string.Empty;
    private bool _IsRunning;
    private double _UiFontSize = 14;
    private TestRunState _LastRunState = TestRunState.None;
    private readonly Dispatcher _UiDispatcher;
    private readonly StringBuilder _OutputBuilder = new();
    private readonly ConcurrentQueue<string> _PendingOutputLines = new();
    private int _IsOutputFlushScheduled;
    private bool _IsRestoringSettings;
    private string? _PendingRestoredConfiguration;
    private CancellationTokenSource? _RunCancellation;
    private RunStopReason _RunStopReason;

    /// <summary>
    /// How long every application started by a run must stay closed before the run is treated
    /// as abandoned. UI tests close the application between test cases, so a short absence is
    /// normal and must not end the run.
    /// </summary>
    private static readonly TimeSpan APP_UNDER_TEST_ABSENCE_GRACE_PERIOD = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often the process tree is polled while a run is in progress.
    /// </summary>
    private static readonly TimeSpan APP_UNDER_TEST_POLL_INTERVAL = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long to keep draining a finished process's redirected output before abandoning it.
    /// A descendant process (such as an application launched by a UI test) inherits the
    /// output pipes, so the readers can stay open indefinitely after <c>dotnet</c> itself
    /// exits. Without this bound the run would never be observed as finished.
    /// </summary>
    private static readonly TimeSpan OUTPUT_DRAIN_GRACE_PERIOD = TimeSpan.FromSeconds(5);

    public MainWindowViewModel(
        ISettingsService settingsService,
        ITestRunLogger testRunLogger,
        IProcessTreeInspector processTreeInspector)
    {
        _SettingsService = settingsService;
        _TestRunLogger = testRunLogger;
        _ProcessTreeInspector = processTreeInspector;
        _UiDispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        BrowseTargetCommand = new RelayCommand(BrowseTarget);
        RefreshTestsCommand = new AsyncRelayCommand(LoadTestsAsync, CanExecuteTestCommands);
        RunAllCommand = new AsyncRelayCommand(RunAllTestsAsync, CanExecuteTestCommands);
        RunSelectedCommand = new AsyncRelayCommand(RunSelectedTestsAsync, CanExecuteRunSelected);
        ClearSelectionCommand = new RelayCommand(ClearSelection, CanClearSelection);
        RunClassCommand = new AsyncRelayCommand<TestClassNode>(RunClassNodeAsync, CanExecuteRunClassNode);
        RunMethodCommand = new AsyncRelayCommand<TestMethodNode>(RunMethodNodeAsync, CanExecuteRunMethodNode);
        ClearLogCommand = new RelayCommand(ClearLog);
        StopCommand = new RelayCommand(StopRun, CanStopRun);

        foreach (var configuration in _DefaultConfigurations)
        {
            Configurations.Add(configuration);
        }

        var persistedSettings = _SettingsService.Load();
        var initialTarget = ResolveInitialTargetPath(persistedSettings.TargetPath);

        if (!string.IsNullOrWhiteSpace(initialTarget))
        {
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
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> Configurations { get; } = [];

    public ObservableCollection<TestClassNode> TestClasses { get; } = [];

    public RelayCommand BrowseTargetCommand { get; }

    public AsyncRelayCommand RefreshTestsCommand { get; }

    public AsyncRelayCommand RunAllCommand { get; }

    public AsyncRelayCommand RunSelectedCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public AsyncRelayCommand<TestClassNode> RunClassCommand { get; }

    public AsyncRelayCommand<TestMethodNode> RunMethodCommand { get; }

    public RelayCommand ClearLogCommand { get; }

    public RelayCommand StopCommand { get; }

    public string TargetPath
    {
        get => _TargetPath;
        set
        {
            if (SetProperty(ref _TargetPath, value))
            {
                NotifyCommandStateChanged();

                if (!_IsRestoringSettings)
                {
                    SavePersistedSettings();
                }
            }
        }
    }

    public string SelectedConfiguration
    {
        get => _SelectedConfiguration;
        set
        {
            if (SetProperty(ref _SelectedConfiguration, value) && !_IsRestoringSettings)
            {
                SavePersistedSettings();
            }
        }
    }

    public string StatusText
    {
        get => _StatusText;
        set => SetProperty(ref _StatusText, value);
    }

    public string OutputLog
    {
        get => _OutputLog;
        set => SetProperty(ref _OutputLog, value);
    }

    public double UiFontSize
    {
        get => _UiFontSize;
        set
        {
            var clamped = Math.Clamp(value, 11, 26);

            if (SetProperty(ref _UiFontSize, clamped))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TreeIconSize)));
            }
        }
    }

    public double TreeIconSize => UiFontSize + 3;

    public bool IsRunning
    {
        get => _IsRunning;
        set
        {
            if (SetProperty(ref _IsRunning, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public TestRunState LastRunState
    {
        get => _LastRunState;
        set
        {
            if (SetProperty(ref _LastRunState, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunInProgress)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLastRunPassed)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLastRunFailed)));
            }
        }
    }

    public bool IsRunInProgress => LastRunState == TestRunState.Running;

    public bool IsLastRunPassed => LastRunState == TestRunState.Passed;

    public bool IsLastRunFailed => LastRunState == TestRunState.Failed;

    public void AdjustFontSize(double delta)
    {
        UiFontSize += delta;
    }

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

    private async Task LoadTestsAsync()
    {
        if (!CanExecuteTestCommands())
        {
            return;
        }

        IsRunning = true;

        using var discoveryCancellation = new CancellationTokenSource();
        _RunCancellation = discoveryCancellation;
        StopCommand.NotifyCanExecuteChanged();

        try
        {
            StatusText = "Discovering tests...";
            AppendOutput($"{Environment.NewLine}--- Discover tests ({SelectedConfiguration}) ---");
            AppendOutput("Discovering tests. This can take a moment for large solutions.");

            var outputLines = new List<string>();
            var discoverArgs = $"test \"{TargetPath}\" -c {SelectedConfiguration} --list-tests --nologo";

            AppendOutput($"> dotnet {discoverArgs}");

            var exitCode = await RunDotnetCommandAsync(
                discoverArgs,
                line => outputLines.Add(line),
                TimeSpan.FromMinutes(2),
                discoveryCancellation);

            if (discoveryCancellation.IsCancellationRequested)
            {
                StatusText = "Test discovery stopped";
                AppendOutput(StatusText);
                return;
            }

            var parsedTests = ParseTests(outputLines)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static test => test, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resolvedEntries = await ResolveTestEntriesAsync(parsedTests);
            RebuildTestTree(resolvedEntries);

            if (exitCode != 0)
            {
                foreach (var line in outputLines.TakeLast(30))
                {
                    AppendOutput(line);
                }
            }

            StatusText = exitCode == 0
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

    public async Task RunClassAsync(string className)
    {
        if (!CanExecuteTestCommands() || string.IsNullOrWhiteSpace(className))
        {
            return;
        }

        var matchingClassNodes = TestClasses
            .Where(node => string.Equals(node.ClassName, className, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var classNode in matchingClassNodes)
        {
            classNode.RunState = TestRunState.Running;

            foreach (var methodNode in classNode.Methods)
            {
                methodNode.RunState = TestRunState.Running;
            }
        }

        var selectedClassMethods = matchingClassNodes
            .SelectMany(static classNode => classNode.Methods)
            .Select(static methodNode => methodNode.FullyQualifiedName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selectedClassMethods.Count == 0)
        {
            return;
        }

        var classFilterParts = selectedClassMethods
            .Select(static testName => $"FullyQualifiedName~{EscapeFilterValue(testName)}");

        var classFilter = string.Join("|", classFilterParts);
        var capturedLines = new List<string>();
        var header = $"Running class {className}";
        var outcome = await RunTestsAsync(classFilter, header, capturedLines);

        var allClassMethods = matchingClassNodes
            .SelectMany(static classNode => classNode.Methods)
            .ToList();

        if (outcome.WasStopped)
        {
            ResetRunStates(allClassMethods, matchingClassNodes);
            return;
        }

        ApplyMethodResults(allClassMethods, capturedLines, outcome.ExitCode);
        LogRunSummary(header, allClassMethods, capturedLines);

        foreach (var classNode in matchingClassNodes)
        {
            UpdateClassRunStateFromMethods(classNode);
        }
    }

    public async Task RunMethodAsync(string fullyQualifiedName)
    {
        if (!CanExecuteTestCommands() || string.IsNullOrWhiteSpace(fullyQualifiedName))
        {
            return;
        }

        var matchingMethods = TestClasses
            .SelectMany(static classNode => classNode.Methods)
            .Where(node => string.Equals(node.FullyQualifiedName, fullyQualifiedName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var methodNode in matchingMethods)
        {
            methodNode.RunState = TestRunState.Running;
        }

        foreach (var classNode in TestClasses.Where(classNode => classNode.Methods.Any(
                     methodNode => string.Equals(methodNode.FullyQualifiedName, fullyQualifiedName, StringComparison.OrdinalIgnoreCase))))
        {
            classNode.RunState = TestRunState.Running;
        }

        var exactFilter = $"FullyQualifiedName={EscapeFilterValue(fullyQualifiedName)}";
        var header = $"Running method {fullyQualifiedName}";
        var capturedLines = new List<string>();
        var outcome = await RunTestsAsync(exactFilter, header, capturedLines);

        var affectedClasses = TestClasses
            .Where(classNode => classNode.Methods.Any(
                methodNode => string.Equals(methodNode.FullyQualifiedName, fullyQualifiedName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (outcome.WasStopped)
        {
            ResetRunStates(matchingMethods, affectedClasses);
            return;
        }

        var methodState = outcome.ExitCode == 0 ? TestRunState.Passed : TestRunState.Failed;

        foreach (var methodNode in matchingMethods)
        {
            methodNode.RunState = methodState;
        }

        LogRunSummary(header, matchingMethods, capturedLines);

        foreach (var classNode in affectedClasses)
        {
            UpdateClassRunStateFromMethods(classNode);
        }
    }

    private Task RunClassNodeAsync(TestClassNode classNode)
    {
        return RunClassAsync(classNode.ClassName);
    }

    private Task RunMethodNodeAsync(TestMethodNode methodNode)
    {
        return RunMethodAsync(methodNode.FullyQualifiedName);
    }

    private bool CanExecuteRunClassNode(TestClassNode classNode)
    {
        return CanExecuteTestCommands() && !string.IsNullOrWhiteSpace(classNode.ClassName);
    }

    private bool CanExecuteRunMethodNode(TestMethodNode methodNode)
    {
        return CanExecuteTestCommands() && !string.IsNullOrWhiteSpace(methodNode.FullyQualifiedName);
    }

    private async Task RunAllTestsAsync()
    {
        if (!CanExecuteTestCommands())
        {
            return;
        }

        foreach (var classNode in TestClasses)
        {
            classNode.RunState = TestRunState.Running;

            foreach (var methodNode in classNode.Methods)
            {
                methodNode.RunState = TestRunState.Running;
            }
        }

        var capturedLines = new List<string>();
        var outcome = await RunTestsAsync(null, "Running all tests", capturedLines);
        var allMethods = TestClasses.SelectMany(static classNode => classNode.Methods).ToList();

        if (outcome.WasStopped)
        {
            ResetRunStates(allMethods, TestClasses);
            return;
        }

        ApplyMethodResults(allMethods, capturedLines, outcome.ExitCode);

        foreach (var classNode in TestClasses)
        {
            UpdateClassRunStateFromMethods(classNode);
        }

        LogRunSummary("Running all tests", allMethods, capturedLines);
    }

    private async Task RunSelectedTestsAsync()
    {
        if (!CanExecuteTestCommands())
        {
            return;
        }

        var selectedMethods = GetSelectedMethods();

        if (selectedMethods.Count == 0)
        {
            return;
        }

        var affectedClasses = TestClasses
            .Where(static classNode => classNode.Methods.Any(static methodNode => methodNode.IsSelected))
            .ToList();

        foreach (var methodNode in selectedMethods)
        {
            methodNode.RunState = TestRunState.Running;
        }

        foreach (var classNode in affectedClasses)
        {
            classNode.RunState = TestRunState.Running;
        }

        var filterParts = selectedMethods
            .Select(static methodNode => methodNode.FullyQualifiedName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static testName => $"FullyQualifiedName={EscapeFilterValue(testName)}");

        var filter = string.Join("|", filterParts);
        var header = selectedMethods.Count == 1
            ? $"Running selected test {selectedMethods[0].FullyQualifiedName}"
            : $"Running {selectedMethods.Count} selected tests";

        var capturedLines = new List<string>();
        var outcome = await RunTestsAsync(filter, header, capturedLines);

        if (outcome.WasStopped)
        {
            ResetRunStates(selectedMethods, affectedClasses);
            return;
        }

        ApplyMethodResults(selectedMethods, capturedLines, outcome.ExitCode);
        LogRunSummary(header, selectedMethods, capturedLines);

        foreach (var classNode in affectedClasses)
        {
            UpdateClassRunStateFromMethods(classNode);
        }
    }

    /// <summary>
    /// Clears the run state of the given nodes back to "not run". Used when a run is stopped,
    /// because the results of an interrupted run are unknown and must not be shown as
    /// failures.
    /// </summary>
    private static void ResetRunStates(
        IEnumerable<TestMethodNode> methods,
        IEnumerable<TestClassNode> classes)
    {
        foreach (var methodNode in methods)
        {
            methodNode.RunState = TestRunState.None;
        }

        foreach (var classNode in classes)
        {
            UpdateClassRunStateFromMethods(classNode);
        }
    }

    private static void ApplyMethodResults(
        IReadOnlyCollection<TestMethodNode> methods,
        IReadOnlyCollection<string> capturedLines,
        int exitCode)
    {
        if (exitCode == 0)
        {
            foreach (var methodNode in methods)
            {
                methodNode.RunState = TestRunState.Passed;
            }

            return;
        }

        var failedNames = ParseFailedTestNames(capturedLines);

        if (failedNames.Count == 0)
        {
            foreach (var methodNode in methods)
            {
                methodNode.RunState = TestRunState.Failed;
            }

            return;
        }

        foreach (var methodNode in methods)
        {
            methodNode.RunState = IsMethodFailed(methodNode, failedNames)
                ? TestRunState.Failed
                : TestRunState.Passed;
        }
    }

    private static List<string> ParseFailedTestNames(IEnumerable<string> outputLines)
    {
        var failed = new List<string>();

        foreach (var rawLine in outputLines)
        {
            var line = rawLine.Trim();

            if (!line.StartsWith("Failed ", StringComparison.Ordinal))
            {
                continue;
            }

            var rest = line.Substring("Failed ".Length).Trim();

            if (rest.Length == 0)
            {
                continue;
            }

            var bracketIndex = rest.IndexOf(" [", StringComparison.Ordinal);

            if (bracketIndex >= 0)
            {
                rest = rest.Substring(0, bracketIndex);
            }

            rest = rest.Trim();

            if (rest.Length > 0)
            {
                failed.Add(rest);
            }
        }

        return failed;
    }

    private static bool IsMethodFailed(TestMethodNode methodNode, IReadOnlyCollection<string> failedNames)
    {
        return failedNames.Any(failedName => DoesReportedTestNameMatch(failedName, methodNode));
    }

    /// <summary>
    /// Determines whether a raw test name reported by <c>dotnet test</c> (e.g. from a
    /// "Failed " console line) refers to the given method node, accounting for parameterized
    /// test name suffixes.
    /// </summary>
    private static bool DoesReportedTestNameMatch(string reportedName, TestMethodNode methodNode)
    {
        var fullyQualifiedName = methodNode.FullyQualifiedName;
        var candidate = reportedName;
        var parenIndex = candidate.IndexOf('(');

        if (parenIndex >= 0)
        {
            candidate = candidate.Substring(0, parenIndex);
        }

        candidate = candidate.Trim();

        if (candidate.Length == 0)
        {
            return false;
        }

        return string.Equals(candidate, fullyQualifiedName, StringComparison.OrdinalIgnoreCase)
            || fullyQualifiedName.EndsWith("." + candidate, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, methodNode.MethodName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds the error and stack trace captured for a failed method, matching by the raw test
    /// names reported by <c>dotnet test</c>.
    /// </summary>
    private static (string? ErrorMessage, string? StackTrace) FindFailureDetails(
        TestMethodNode methodNode,
        IReadOnlyDictionary<string, (string? ErrorMessage, string? StackTrace)> failureDetailsByReportedName)
    {
        foreach (var (reportedName, details) in failureDetailsByReportedName)
        {
            if (DoesReportedTestNameMatch(reportedName, methodNode))
            {
                return details;
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Scans <c>dotnet test</c> console output for "Failed &lt;test&gt;" blocks and extracts
    /// their error message and stack trace sections, keyed by the raw reported test name.
    /// </summary>
    private static Dictionary<string, (string? ErrorMessage, string? StackTrace)> ParseFailureDetails(
        IEnumerable<string> outputLines)
    {
        var lines = outputLines as IReadOnlyList<string> ?? outputLines.ToList();
        var detailsByReportedName =
            new Dictionary<string, (string? ErrorMessage, string? StackTrace)>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();

            if (!line.StartsWith("Failed ", StringComparison.Ordinal))
            {
                continue;
            }

            var rest = line.Substring("Failed ".Length).Trim();
            var bracketIndex = rest.IndexOf(" [", StringComparison.Ordinal);

            if (bracketIndex >= 0)
            {
                rest = rest.Substring(0, bracketIndex);
            }

            rest = rest.Trim();

            if (rest.Length == 0)
            {
                continue;
            }

            var details = ExtractFailureDetails(lines, i + 1);

            if (details.ErrorMessage is not null || details.StackTrace is not null)
            {
                detailsByReportedName[rest] = details;
            }
        }

        return detailsByReportedName;
    }

    /// <summary>
    /// Reads the error and stack trace sections following a "Failed " line, stopping at the
    /// start of the next test's output.
    /// </summary>
    private static (string? ErrorMessage, string? StackTrace) ExtractFailureDetails(
        IReadOnlyList<string> lines,
        int startIndex)
    {
        var errorMessageLines = new List<string>();
        var stackTraceLines = new List<string>();
        var section = FailureOutputSection.None;

        for (var i = startIndex; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();

            if (trimmed.StartsWith("Failed ", StringComparison.Ordinal) ||
                trimmed.StartsWith("Passed ", StringComparison.Ordinal))
            {
                break;
            }

            if (string.Equals(trimmed, "Error Message:", StringComparison.OrdinalIgnoreCase))
            {
                section = FailureOutputSection.ErrorMessage;
                continue;
            }

            if (string.Equals(trimmed, "Stack Trace:", StringComparison.OrdinalIgnoreCase))
            {
                section = FailureOutputSection.StackTrace;
                continue;
            }

            if (trimmed.Length == 0)
            {
                if (section == FailureOutputSection.StackTrace)
                {
                    break;
                }

                continue;
            }

            if (section == FailureOutputSection.ErrorMessage)
            {
                errorMessageLines.Add(trimmed);
            }
            else if (section == FailureOutputSection.StackTrace)
            {
                stackTraceLines.Add(trimmed);
            }
        }

        return (
            errorMessageLines.Count > 0 ? string.Join(" ", errorMessageLines) : null,
            stackTraceLines.Count > 0 ? string.Join(Environment.NewLine, stackTraceLines) : null);
    }

    private enum FailureOutputSection
    {
        None,
        ErrorMessage,
        StackTrace,
    }

    private List<TestMethodNode> GetSelectedMethods()
    {
        return TestClasses
            .SelectMany(static classNode => classNode.Methods)
            .Where(static methodNode => methodNode.IsSelected)
            .ToList();
    }

    private bool HasSelectedMethods()
    {
        return TestClasses.Any(static classNode => classNode.Methods.Any(static methodNode => methodNode.IsSelected));
    }

    private bool CanExecuteRunSelected()
    {
        return CanExecuteTestCommands() && HasSelectedMethods();
    }

    private void ClearSelection()
    {
        foreach (var classNode in TestClasses)
        {
            classNode.IsSelected = false;
        }
    }

    private bool CanClearSelection()
    {
        return HasSelectedMethods();
    }

    private void OnNodeSelectionChanged(object? sender, EventArgs e)
    {
        RunSelectedCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
    }

    private async Task<TestRunOutcome> RunTestsAsync(string? filter, string header, List<string>? capturedLines = null)
    {
        IsRunning = true;
        LastRunState = TestRunState.Running;

        using var runCancellation = new CancellationTokenSource();
        _RunCancellation = runCancellation;
        _RunStopReason = RunStopReason.None;
        StopCommand.NotifyCanExecuteChanged();

        var targetPath = TargetPath;
        var configuration = SelectedConfiguration;
        var stopwatch = Stopwatch.StartNew();

        _TestRunLogger.LogTestRunStarted(header, targetPath, configuration, filter);

        try
        {
            StatusText = "Running tests...";
            AppendOutput($"{Environment.NewLine}--- {header} ({SelectedConfiguration}) ---");

            var command = $"test \"{TargetPath}\" -c {SelectedConfiguration} --nologo";

            if (!string.IsNullOrWhiteSpace(filter))
            {
                command += $" --filter \"{filter}\"";
            }

            AppendOutput($"> dotnet {command}");

            Action<string> sink = AppendOutput;

            if (capturedLines is not null)
            {
                sink = line =>
                {
                    lock (capturedLines)
                    {
                        capturedLines.Add(line);
                    }

                    AppendOutput(line);
                };
            }

            var exitCode = await RunDotnetCommandAsync(
                command,
                sink,
                runCancellation: runCancellation,
                watchForClosedAppUnderTest: true);

            if (runCancellation.IsCancellationRequested)
            {
                var wasAppClosed = _RunStopReason == RunStopReason.ApplicationClosed;
                StatusText = wasAppClosed
                    ? "Test run stopped: application closed"
                    : "Test run stopped";
                LastRunState = TestRunState.None;
                _TestRunLogger.LogTestRunStopped(header, targetPath, configuration, stopwatch.Elapsed, DescribeStopReason(_RunStopReason));
                return new TestRunOutcome(exitCode, WasStopped: true);
            }

            StatusText = exitCode == 0 ? "Test run completed" : "Test run failed";
            LastRunState = exitCode == 0 ? TestRunState.Passed : TestRunState.Failed;
            _TestRunLogger.LogTestRunCompleted(header, targetPath, configuration, exitCode, stopwatch.Elapsed);
            return new TestRunOutcome(exitCode, WasStopped: false);
        }
        catch (Exception ex)
        {
            AppendOutput($"Test run failed unexpectedly: {ex.Message}");
            StatusText = "Test run failed";
            LastRunState = TestRunState.Failed;
            _TestRunLogger.LogTestRunError(header, targetPath, configuration, ex);
            return new TestRunOutcome(-1, WasStopped: false);
        }
        finally
        {
            _RunCancellation = null;
            IsRunning = false;
        }
    }

    /// <summary>
    /// Logs the pass/fail counts for a completed run and, for each failed test, the error
    /// message and stack trace extracted from the captured <c>dotnet test</c> output when
    /// available.
    /// </summary>
    private void LogRunSummary(string header, IReadOnlyCollection<TestMethodNode> methods, IReadOnlyCollection<string>? capturedLines = null)
    {
        var passedCount = methods.Count(static methodNode => methodNode.RunState == TestRunState.Passed);
        var failedCount = methods.Count(static methodNode => methodNode.RunState == TestRunState.Failed);
        var failureDetails = capturedLines is not null ? ParseFailureDetails(capturedLines) : [];
        var failedTests = methods
            .Where(static methodNode => methodNode.RunState == TestRunState.Failed)
            .Select(methodNode =>
            {
                var details = FindFailureDetails(methodNode, failureDetails);
                return new FailedTestDetail(
                    methodNode.FullyQualifiedName,
                    details.ErrorMessage,
                    details.StackTrace);
            })
            .ToList();

        _TestRunLogger.LogTestRunSummary(header, passedCount, failedCount, failedTests);
    }

    private static void UpdateClassRunStateFromMethods(TestClassNode classNode)
    {
        if (classNode.Methods.Any(static methodNode => methodNode.RunState == TestRunState.Running))
        {
            classNode.RunState = TestRunState.Running;
            return;
        }

        if (classNode.Methods.Any(static methodNode => methodNode.RunState == TestRunState.Failed))
        {
            classNode.RunState = TestRunState.Failed;
            return;
        }

        if (classNode.Methods.Any(static methodNode => methodNode.RunState == TestRunState.Passed))
        {
            classNode.RunState = TestRunState.Passed;
            return;
        }

        classNode.RunState = TestRunState.None;
    }

    private void SetTargetPath(string targetPath)
    {
        TargetPath = targetPath;

        var configurations = ReadConfigurations(targetPath).ToList();

        if (configurations.Count == 0)
        {
            configurations.AddRange(_DefaultConfigurations);
        }

        Configurations.Clear();

        foreach (var configuration in configurations)
        {
            Configurations.Add(configuration);
        }

        var restoredConfiguration = _PendingRestoredConfiguration is { Length: > 0 }
            ? Configurations.FirstOrDefault(configuration => string.Equals(configuration, _PendingRestoredConfiguration, StringComparison.OrdinalIgnoreCase))
            : null;

        var preferredConfiguration = Configurations
            .FirstOrDefault(static configuration => string.Equals(configuration, "MIQA", StringComparison.OrdinalIgnoreCase));

        SelectedConfiguration = restoredConfiguration ?? preferredConfiguration ?? Configurations[0];

        TestClasses.Clear();

        AppendOutput($"Using test target: {targetPath}");
        StatusText = "Target selected. Discovering tests...";
        _ = LoadTestsAfterTargetSelectionAsync();
    }

    /// <summary>
    /// Persists the current target path and selected configuration so they can be
    /// restored the next time the application starts.
    /// </summary>
    private void SavePersistedSettings()
    {
        _SettingsService.Save(new TestRunnerSettings
        {
            TargetPath = TargetPath,
            SelectedConfiguration = SelectedConfiguration
        });
    }

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

    private bool CanExecuteTestCommands()
    {
        return !IsRunning
            && !string.IsNullOrWhiteSpace(TargetPath)
            && File.Exists(TargetPath);
    }

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

    /// <summary>
    /// Describes why a run was stopped, for the run log.
    /// </summary>
    private static string DescribeStopReason(RunStopReason reason)
    {
        return reason switch
        {
            RunStopReason.ApplicationClosed => "Application under test closed",
            RunStopReason.User => "Stopped by user",
            _ => "Stopped"
        };
    }

    private bool CanStopRun()
    {
        return IsRunning && _RunCancellation is { IsCancellationRequested: false };
    }

    private void ClearLog()
    {
        while (_PendingOutputLines.TryDequeue(out _))
        {
        }

        _OutputBuilder.Clear();
        OutputLog = string.Empty;
    }

    private static string EscapeFilterValue(string value)
    {
        return value.Replace("\"", "\\\"");
    }

    private static IEnumerable<string> ParseTests(IEnumerable<string> outputLines)
    {
        var lines = outputLines.ToList();
        var inTestSection = false;
        var discoveredTests = new List<string>();

        foreach (var line in lines)
        {
            if (line.Contains("The following Tests are available", StringComparison.OrdinalIgnoreCase))
            {
                inTestSection = true;
                continue;
            }

            if (!inTestSection)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("Test run", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Total tests", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Workload updates", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var trimmed = line.Trim();

            if (IsPotentialTestName(trimmed))
            {
                discoveredTests.Add(trimmed);
            }
        }

        if (discoveredTests.Count > 0)
        {
            return discoveredTests;
        }

        return lines
            .Select(static line => line.Trim())
            .Where(IsPotentialTestName)
            .ToList();
    }

    private static bool IsPotentialTestName(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        if (line.Contains(" -> ", StringComparison.Ordinal)
            || line.Contains('\\')
            || line.Contains(' ')
            || line.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || line.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Test run", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Total tests", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Passed!", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Build", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Restore", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Workload", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("VSTest", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Starting", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TestNameRegex().IsMatch(line);
    }

    private void RebuildTestTree(IEnumerable<TestDiscoveryEntry> testNames)
    {
        var grouped = testNames
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.ClassName))
            .GroupBy(static entry => entry.ClassName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var existingClassNode in TestClasses)
        {
            existingClassNode.SelectionChanged -= OnNodeSelectionChanged;
        }

        TestClasses.Clear();

        foreach (var group in grouped)
        {
            var classNode = new TestClassNode(group.Key);

            foreach (var entry in group.OrderBy(static e => e.MethodName, StringComparer.OrdinalIgnoreCase))
            {
                var methodNode = new TestMethodNode(
                    entry.MethodName,
                    entry.FullyQualifiedName)
                {
                    Owner = classNode
                };

                classNode.Methods.Add(methodNode);
            }

            classNode.SelectionChanged += OnNodeSelectionChanged;
            TestClasses.Add(classNode);
        }

        NotifyCommandStateChanged();
    }

    private async Task<List<TestDiscoveryEntry>> ResolveTestEntriesAsync(IReadOnlyCollection<string> discoveredTests)
    {
        var normalized = discoveredTests
            .Select(static test => test.Trim())
            .Where(static test => !string.IsNullOrWhiteSpace(test))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            return [];
        }

        var hasFullyQualifiedNames = normalized.Any(static test => test.Contains('.'));

        if (hasFullyQualifiedNames)
        {
            return normalized.Select(ToEntryFromFullyQualified).ToList();
        }

        var sourceIndex = await Task.Run(() => BuildSourceTestIndex(TargetPath));

        if (sourceIndex.Count == 0)
        {
            return normalized.Select(ToFallbackEntry).ToList();
        }

        var methodsByName = sourceIndex
            .GroupBy(static item => item.MethodName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => new Queue<SourceTestMethod>(group.OrderBy(static method => method.ClassName, StringComparer.OrdinalIgnoreCase))
            );

        var resolvedEntries = new List<TestDiscoveryEntry>(normalized.Count);

        foreach (var listedTest in normalized)
        {
            var methodKey = NormalizeListedMethodName(listedTest);

            if (methodsByName.TryGetValue(methodKey, out var queue) && queue.Count > 0)
            {
                var matched = queue.Dequeue();
                resolvedEntries.Add(new TestDiscoveryEntry(matched.ClassName, matched.MethodName, matched.FullyQualifiedName));
                continue;
            }

            resolvedEntries.Add(ToFallbackEntry(listedTest));
        }

        return resolvedEntries;
    }

    private static TestDiscoveryEntry ToEntryFromFullyQualified(string fullyQualified)
    {
        var lastSeparatorIndex = fullyQualified.LastIndexOf('.');

        if (lastSeparatorIndex <= 0 || lastSeparatorIndex == fullyQualified.Length - 1)
        {
            return ToFallbackEntry(fullyQualified);
        }

        var className = fullyQualified[..lastSeparatorIndex];
        var methodName = fullyQualified[(lastSeparatorIndex + 1)..];
        return new TestDiscoveryEntry(className, methodName, fullyQualified);
    }

    private static TestDiscoveryEntry ToFallbackEntry(string listedTest)
    {
        var firstSeparatorIndex = listedTest.IndexOf('_');
        var className = firstSeparatorIndex > 0 ? listedTest[..firstSeparatorIndex] : "Ungrouped";
        return new TestDiscoveryEntry(className, listedTest, listedTest);
    }

    private static string NormalizeListedMethodName(string listedTest)
    {
        var method = listedTest;
        var openParenthesisIndex = method.IndexOf('(');

        if (openParenthesisIndex > 0)
        {
            method = method[..openParenthesisIndex];
        }

        var lastSeparatorIndex = method.LastIndexOf('.');

        if (lastSeparatorIndex >= 0 && lastSeparatorIndex < method.Length - 1)
        {
            method = method[(lastSeparatorIndex + 1)..];
        }

        return method.Trim();
    }

    private static List<SourceTestMethod> BuildSourceTestIndex(string targetPath)
    {
        var projectFiles = ResolveTargetProjectFiles(targetPath);
        var discoveredMethods = new List<SourceTestMethod>();

        foreach (var projectFile in projectFiles)
        {
            var projectDirectory = Path.GetDirectoryName(projectFile);

            if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
            {
                continue;
            }

            foreach (var sourceFile in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
            {
                if (sourceFile.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)
                    || sourceFile.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                discoveredMethods.AddRange(ExtractTestsFromSourceFile(sourceFile));
            }
        }

        return discoveredMethods;
    }

    private static List<string> ResolveTargetProjectFiles(string targetPath)
    {
        if (targetPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return [targetPath];
        }

        if (!targetPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || !File.Exists(targetPath))
        {
            return [];
        }

        var solutionDirectory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        var projectFiles = new List<string>();

        foreach (var line in File.ReadLines(targetPath))
        {
            if (!line.Contains(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = line.Split(',');

            if (parts.Length < 2)
            {
                continue;
            }

            var relativePath = parts[1].Trim().Trim('"').Replace('\\', Path.DirectorySeparatorChar);

            if (!relativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var absolutePath = Path.GetFullPath(Path.Combine(solutionDirectory, relativePath));

            if (File.Exists(absolutePath))
            {
                projectFiles.Add(absolutePath);
            }
        }

        return projectFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<SourceTestMethod> ExtractTestsFromSourceFile(string sourceFile)
    {
        var methods = new List<SourceTestMethod>();
        var sourceText = File.ReadAllText(sourceFile);
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();

        var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

        foreach (var method in methodDeclarations)
        {
            if (!HasTestAttribute(method))
            {
                continue;
            }

            var typeChain = method.Ancestors()
                .OfType<TypeDeclarationSyntax>()
                .Reverse()
                .Select(static type => type.Identifier.Text)
                .ToList();

            if (typeChain.Count == 0)
            {
                continue;
            }

            var classPath = string.Join('.', typeChain);
            var namespacePath = GetNamespacePath(method);
            var fullyQualifiedClass = string.IsNullOrWhiteSpace(namespacePath)
                ? classPath
                : string.Concat(namespacePath, ".", classPath);

            var methodName = method.Identifier.Text;

            methods.Add(new SourceTestMethod(
                fullyQualifiedClass,
                methodName,
                string.Concat(fullyQualifiedClass, ".", methodName)));
        }

        return methods;
    }

    private static bool HasTestAttribute(MethodDeclarationSyntax method)
    {
        var attributes = method.AttributeLists.SelectMany(static list => list.Attributes);

        foreach (var attribute in attributes)
        {
            var name = attribute.Name.ToString();

            if (name.EndsWith("TestMethod", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("DataTestMethod", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Fact", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Theory", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Test", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("TestCase", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("TestCaseSource", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetNamespacePath(SyntaxNode node)
    {
        var namespaces = node.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(static ns => ns.Name.ToString())
            .ToList();

        return string.Join('.', namespaces);
    }

    private sealed record TestDiscoveryEntry(string ClassName, string MethodName, string FullyQualifiedName);

    private sealed record SourceTestMethod(string ClassName, string MethodName, string FullyQualifiedName);

    private static IEnumerable<string> ReadConfigurations(string targetPath)
    {
        if (!targetPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        try
        {
            var document = XDocument.Load(targetPath);

            return document.Descendants()
                .Where(element => element.Name.LocalName == "Configurations")
                .SelectMany(element => (element.Value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Resolves the target path to select at startup, preferring a previously persisted
    /// path (if it still exists on disk) over the default auto-discovery walk-up.
    /// </summary>
    private static string? ResolveInitialTargetPath(string? persistedTargetPath)
    {
        if (!string.IsNullOrWhiteSpace(persistedTargetPath) && File.Exists(persistedTargetPath))
        {
            return persistedTargetPath;
        }

        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            var solutionPath = Path.Combine(currentDirectory.FullName, "CSM.UI.Tests.sln");

            if (File.Exists(solutionPath))
            {
                return solutionPath;
            }

            var projectPath = Path.Combine(currentDirectory.FullName, "CSM.UI.Tests.csproj");

            if (File.Exists(projectPath))
            {
                return projectPath;
            }

            currentDirectory = currentDirectory.Parent;
        }

        return null;
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

    private async Task<int> RunDotnetCommandAsync(
        string args,
        Action<string> onOutput,
        TimeSpan? timeout = null,
        CancellationTokenSource? runCancellation = null,
        bool watchForClosedAppUnderTest = false)
    {
        var cancellationToken = runCancellation?.Token ?? CancellationToken.None;
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(TargetPath) ?? Environment.CurrentDirectory
        };

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();

        var outputTask = ReadStreamAsync(process.StandardOutput, onOutput);
        var errorTask = ReadStreamAsync(process.StandardError, onOutput);
        var drainTask = Task.WhenAll(outputTask, errorTask);

        using var exitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (timeout.HasValue)
        {
            exitCancellation.CancelAfter(timeout.Value);
        }

        using var watchdogCancellation = new CancellationTokenSource();
        var watchdogTask = watchForClosedAppUnderTest && runCancellation is not null
            ? WatchForClosedAppUnderTestAsync(process.Id, runCancellation, onOutput, watchdogCancellation.Token)
            : Task.CompletedTask;

        try
        {
            await process.WaitForExitAsync(exitCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            await StopWatchdogAsync(watchdogCancellation, watchdogTask);
            await DrainOutputAsync(drainTask);

            if (cancellationToken.IsCancellationRequested)
            {
                onOutput("Run stopped. The dotnet process and any processes it started were terminated.");
            }
            else
            {
                onOutput($"Command timed out after {timeout!.Value.TotalSeconds:0} seconds.");
            }

            return -1;
        }

        await StopWatchdogAsync(watchdogCancellation, watchdogTask);
        await DrainOutputAsync(drainTask);

        return process.ExitCode;
    }

    /// <summary>
    /// Watches the applications started by a run and cancels the run when they all disappear.
    /// UI tests drive an application launched as a descendant of <c>dotnet test</c>; if that
    /// application is closed, the test framework keeps polling for windows that will never
    /// appear and the run would otherwise hang until it is stopped by hand.
    /// </summary>
    private async Task WatchForClosedAppUnderTestAsync(
        int rootProcessId,
        CancellationTokenSource runCancellation,
        Action<string> onOutput,
        CancellationToken watchdogToken)
    {
        var hasSeenAppUnderTest = false;
        DateTime? absentSince = null;

        try
        {
            while (!watchdogToken.IsCancellationRequested && !runCancellation.IsCancellationRequested)
            {
                await Task.Delay(APP_UNDER_TEST_POLL_INTERVAL, watchdogToken);

                var visibleCount = _ProcessTreeInspector.CountDescendantsWithVisibleWindow(rootProcessId);

                if (visibleCount > 0)
                {
                    hasSeenAppUnderTest = true;
                    absentSince = null;
                    continue;
                }

                // Nothing is shown yet during build and test discovery, so the run is only
                // considered abandoned once an application has actually been seen.
                if (!hasSeenAppUnderTest)
                {
                    continue;
                }

                absentSince ??= DateTime.UtcNow;

                if (DateTime.UtcNow - absentSince.Value < APP_UNDER_TEST_ABSENCE_GRACE_PERIOD)
                {
                    continue;
                }

                onOutput(
                    "The application under test is no longer running. Stopping the run after "
                    + $"{APP_UNDER_TEST_ABSENCE_GRACE_PERIOD.TotalSeconds:0} seconds without it.");

                _RunStopReason = RunStopReason.ApplicationClosed;
                runCancellation.Cancel();
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // The run finished or was stopped; the watchdog is no longer needed.
        }
    }

    /// <summary>
    /// Stops the watchdog loop and waits for it to finish so it cannot outlive the run.
    /// </summary>
    private static async Task StopWatchdogAsync(CancellationTokenSource watchdogCancellation, Task watchdogTask)
    {
        await watchdogCancellation.CancelAsync();

        try
        {
            await watchdogTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when the watchdog is cancelled.
        }
    }

    /// <summary>
    /// Waits a bounded time for the redirected output readers to reach end of stream.
    /// Processes started by the tests inherit the output pipes and can outlive
    /// <c>dotnet</c> itself, so the readers are abandoned once the grace period elapses
    /// rather than blocking the run forever.
    /// </summary>
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
    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // The process already exited or cannot be terminated; stopping is best effort.
        }
    }

    private static async Task ReadStreamAsync(StreamReader reader, Action<string> onOutput)
    {
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync();

                if (line is null)
                {
                    break;
                }

                onOutput(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The stream was closed while this reader was abandoned after the grace period.
            // Any remaining output is unrecoverable and must not fault the run.
        }
    }

    private void AppendOutput(string line)
    {
        _PendingOutputLines.Enqueue(line);
        ScheduleOutputFlush();
    }

    private void ScheduleOutputFlush()
    {
        if (Interlocked.Exchange(ref _IsOutputFlushScheduled, 1) == 1)
        {
            return;
        }

        _ = _UiDispatcher.BeginInvoke(FlushPendingOutputLines);
    }

    private void FlushPendingOutputLines()
    {
        try
        {
            var wroteLine = false;

            while (_PendingOutputLines.TryDequeue(out var line))
            {
                if (_OutputBuilder.Length > 0)
                {
                    _OutputBuilder.AppendLine();
                }

                _OutputBuilder.Append(line);
                wroteLine = true;
            }

            if (wroteLine)
            {
                OutputLog = _OutputBuilder.ToString();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _IsOutputFlushScheduled, 0);

            if (!_PendingOutputLines.IsEmpty)
            {
                ScheduleOutputFlush();
            }
        }
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    /// <summary>
    /// Outcome of a <c>dotnet test</c> invocation. <see cref="WasStopped"/> distinguishes a
    /// run the user cancelled from one that genuinely failed, so cancelled runs do not mark
    /// tests as failed.
    /// </summary>
    private readonly record struct TestRunOutcome(int ExitCode, bool WasStopped);

    /// <summary>
    /// Why an in-progress run ended early.
    /// </summary>
    private enum RunStopReason
    {
        None,
        User,
        ApplicationClosed
    }
}

public enum TestRunState
{
    None,
    Running,
    Passed,
    Failed
}

public sealed class TestClassNode : INotifyPropertyChanged
{
    private TestRunState _RunState;
    private bool? _IsSelected = false;
    private bool _IsUpdatingSelection;

    public TestClassNode(string className)
    {
        ClassName = className;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? SelectionChanged;

    public string ClassName { get; }

    public TestRunState RunState
    {
        get => _RunState;
        set
        {
            if (_RunState == value)
            {
                return;
            }

            _RunState = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunState)));
        }
    }

    public bool? IsSelected
    {
        get => _IsSelected;
        set => ApplySelectionToMethods(value ?? false);
    }

    public ObservableCollection<TestMethodNode> Methods { get; } = [];

    public void NotifyMethodSelectionChanged()
    {
        if (_IsUpdatingSelection)
        {
            return;
        }

        RefreshSelectionState();
    }

    private void ApplySelectionToMethods(bool isSelected)
    {
        _IsUpdatingSelection = true;

        foreach (var methodNode in Methods)
        {
            methodNode.IsSelected = isSelected;
        }

        _IsUpdatingSelection = false;
        RefreshSelectionState();
    }

    private void RefreshSelectionState()
    {
        bool? newState;

        if (Methods.Count == 0 || Methods.All(static methodNode => !methodNode.IsSelected))
        {
            newState = false;
        }
        else if (Methods.All(static methodNode => methodNode.IsSelected))
        {
            newState = true;
        }
        else
        {
            newState = null;
        }

        if (_IsSelected != newState)
        {
            _IsSelected = newState;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class TestMethodNode : INotifyPropertyChanged
{
    private TestRunState _RunState;
    private bool _IsSelected;

    public TestMethodNode(
        string methodName,
        string fullyQualifiedName)
    {
        MethodName = methodName;
        FullyQualifiedName = fullyQualifiedName;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string MethodName { get; }

    public string FullyQualifiedName { get; }

    public TestClassNode? Owner { get; set; }

    public TestRunState RunState
    {
        get => _RunState;
        set
        {
            if (_RunState == value)
            {
                return;
            }

            _RunState = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunState)));
        }
    }

    public bool IsSelected
    {
        get => _IsSelected;
        set
        {
            if (_IsSelected == value)
            {
                return;
            }

            _IsSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            Owner?.NotifyMethodSelectionChanged();
        }
    }
}
