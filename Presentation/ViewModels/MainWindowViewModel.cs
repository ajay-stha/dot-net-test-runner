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

    public MainWindowViewModel(ISettingsService settingsService, ITestRunLogger testRunLogger)
    {
        _SettingsService = settingsService;
        _TestRunLogger = testRunLogger;
        _UiDispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        BrowseTargetCommand = new RelayCommand(BrowseTarget);
        RefreshTestsCommand = new AsyncRelayCommand(LoadTestsAsync, CanExecuteTestCommands);
        RunAllCommand = new AsyncRelayCommand(RunAllTestsAsync, CanExecuteTestCommands);
        RunSelectedCommand = new AsyncRelayCommand(RunSelectedTestsAsync, CanExecuteRunSelected);
        ClearSelectionCommand = new RelayCommand(ClearSelection, CanClearSelection);
        RunClassCommand = new AsyncRelayCommand<TestClassNode>(RunClassNodeAsync, CanExecuteRunClassNode);
        RunMethodCommand = new AsyncRelayCommand<TestMethodNode>(RunMethodNodeAsync, CanExecuteRunMethodNode);
        ClearLogCommand = new RelayCommand(ClearLog);

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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TreeIconStrokeThickness)));
            }
        }
    }

    public double TreeIconSize => UiFontSize + 3;

    public double TreeIconStrokeThickness => Math.Max(1.5, Math.Round(UiFontSize * 0.13, 2));

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
                TimeSpan.FromMinutes(2));

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
        var exitCode = await RunTestsAsync(classFilter, header, capturedLines);

        var allClassMethods = matchingClassNodes
            .SelectMany(static classNode => classNode.Methods)
            .ToList();

        ApplyMethodResults(allClassMethods, capturedLines, exitCode);
        LogRunSummary(header, allClassMethods);

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
        var exitCode = await RunTestsAsync(exactFilter, header);
        var methodState = exitCode == 0 ? TestRunState.Passed : TestRunState.Failed;

        foreach (var methodNode in matchingMethods)
        {
            methodNode.RunState = methodState;
        }

        LogRunSummary(header, matchingMethods);

        foreach (var classNode in TestClasses.Where(classNode => classNode.Methods.Any(
                     methodNode => string.Equals(methodNode.FullyQualifiedName, fullyQualifiedName, StringComparison.OrdinalIgnoreCase))))
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

        var exitCode = await RunTestsAsync(null, "Running all tests");
        var runState = exitCode == 0 ? TestRunState.Passed : TestRunState.Failed;

        foreach (var classNode in TestClasses)
        {
            classNode.RunState = runState;

            foreach (var methodNode in classNode.Methods)
            {
                methodNode.RunState = runState;
            }
        }

        LogRunSummary("Running all tests", TestClasses.SelectMany(static classNode => classNode.Methods).ToList());
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
        var exitCode = await RunTestsAsync(filter, header, capturedLines);

        ApplyMethodResults(selectedMethods, capturedLines, exitCode);
        LogRunSummary(header, selectedMethods);

        foreach (var classNode in affectedClasses)
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
        var fullyQualifiedName = methodNode.FullyQualifiedName;

        foreach (var failedName in failedNames)
        {
            var candidate = failedName;
            var parenIndex = candidate.IndexOf('(');

            if (parenIndex >= 0)
            {
                candidate = candidate.Substring(0, parenIndex);
            }

            candidate = candidate.Trim();

            if (candidate.Length == 0)
            {
                continue;
            }

            if (string.Equals(candidate, fullyQualifiedName, StringComparison.OrdinalIgnoreCase)
                || fullyQualifiedName.EndsWith("." + candidate, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, methodNode.MethodName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

    private async Task<int> RunTestsAsync(string? filter, string header, List<string>? capturedLines = null)
    {
        IsRunning = true;
        LastRunState = TestRunState.Running;

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

            var exitCode = await RunDotnetCommandAsync(command, sink);
            StatusText = exitCode == 0 ? "Test run completed" : "Test run failed";
            LastRunState = exitCode == 0 ? TestRunState.Passed : TestRunState.Failed;
            _TestRunLogger.LogTestRunCompleted(header, targetPath, configuration, exitCode, stopwatch.Elapsed);
            return exitCode;
        }
        catch (Exception ex)
        {
            AppendOutput($"Test run failed unexpectedly: {ex.Message}");
            StatusText = "Test run failed";
            LastRunState = TestRunState.Failed;
            _TestRunLogger.LogTestRunError(header, targetPath, configuration, ex);
            return -1;
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>
    /// Logs the pass/fail counts and names of failed tests for a completed run.
    /// </summary>
    private void LogRunSummary(string header, IReadOnlyCollection<TestMethodNode> methods)
    {
        var passedCount = methods.Count(static methodNode => methodNode.RunState == TestRunState.Passed);
        var failedCount = methods.Count(static methodNode => methodNode.RunState == TestRunState.Failed);
        var failedTestNames = methods
            .Where(static methodNode => methodNode.RunState == TestRunState.Failed)
            .Select(static methodNode => methodNode.FullyQualifiedName)
            .ToList();

        _TestRunLogger.LogTestRunSummary(header, passedCount, failedCount, failedTestNames);
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
    }

    private async Task<int> RunDotnetCommandAsync(string args, Action<string> onOutput, TimeSpan? timeout = null)
    {
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
        var processTask = process.WaitForExitAsync();
        var combinedTask = Task.WhenAll(outputTask, errorTask, processTask);

        if (timeout.HasValue)
        {
            var completedTask = await Task.WhenAny(combinedTask, Task.Delay(timeout.Value));

            if (!ReferenceEquals(completedTask, combinedTask))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Best effort timeout stop.
                }

                onOutput($"Command timed out after {timeout.Value.TotalSeconds:0} seconds.");
                return -1;
            }
        }

        await combinedTask;

        return process.ExitCode;
    }

    private static async Task ReadStreamAsync(StreamReader reader, Action<string> onOutput)
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
