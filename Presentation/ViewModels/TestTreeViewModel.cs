using System.Collections.ObjectModel;
using DotNetTestRunner.Domain.Models;
using DotNetTestRunner.Domain.Services;

namespace DotNetTestRunner.Presentation.ViewModels;

/// <summary>
/// The tree of discovered test classes and methods, together with the selection and run-state
/// bookkeeping that operates on it.
/// </summary>
public sealed class TestTreeViewModel
{
    /// <summary>
    /// Raised when the set of selected tests changes.
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Gets the test classes shown in the tree.
    /// </summary>
    public ObservableCollection<TestClassNode> Classes { get; } = [];

    /// <summary>
    /// Replaces the tree with the given discovered tests, grouped by class.
    /// </summary>
    /// <param name="entries">Tests to show.</param>
    public void Rebuild(IEnumerable<TestDiscoveryEntry> entries)
    {
        foreach (var existingClassNode in Classes)
        {
            existingClassNode.SelectionChanged -= OnNodeSelectionChanged;
        }

        Classes.Clear();

        var groups = entries
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.ClassName))
            .GroupBy(static entry => entry.ClassName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var classNode = new TestClassNode(group.Key);

            foreach (var entry in group.OrderBy(static entry => entry.MethodName, StringComparer.OrdinalIgnoreCase))
            {
                classNode.Methods.Add(new TestMethodNode(entry.MethodName, entry.FullyQualifiedName)
                {
                    Owner = classNode
                });
            }

            classNode.SelectionChanged += OnNodeSelectionChanged;
            Classes.Add(classNode);
        }
    }

    /// <summary>
    /// Gets every test method in the tree.
    /// </summary>
    /// <returns>All method nodes.</returns>
    public List<TestMethodNode> GetAllMethods()
    {
        return Classes.SelectMany(static classNode => classNode.Methods).ToList();
    }

    /// <summary>
    /// Gets the test methods the user has selected.
    /// </summary>
    /// <returns>The selected method nodes.</returns>
    public List<TestMethodNode> GetSelectedMethods()
    {
        return Classes
            .SelectMany(static classNode => classNode.Methods)
            .Where(static methodNode => methodNode.IsSelected)
            .ToList();
    }

    /// <summary>
    /// Gets the classes that contain at least one selected test.
    /// </summary>
    /// <returns>The affected class nodes.</returns>
    public List<TestClassNode> GetClassesWithSelectedMethods()
    {
        return Classes
            .Where(static classNode => classNode.Methods.Any(static methodNode => methodNode.IsSelected))
            .ToList();
    }

    /// <summary>
    /// Gets a value indicating whether any test is selected.
    /// </summary>
    /// <returns><see langword="true"/> when at least one test is selected.</returns>
    public bool HasSelectedMethods()
    {
        return Classes.Any(static classNode => classNode.Methods.Any(static methodNode => methodNode.IsSelected));
    }

    /// <summary>
    /// Clears the selection of every test.
    /// </summary>
    public void ClearSelection()
    {
        foreach (var classNode in Classes)
        {
            classNode.IsSelected = false;
        }
    }

    /// <summary>
    /// Finds the classes with a given name.
    /// </summary>
    /// <param name="className">Fully qualified class name to look for.</param>
    /// <returns>The matching class nodes.</returns>
    public List<TestClassNode> FindClasses(string className)
    {
        return Classes
            .Where(classNode => string.Equals(classNode.ClassName, className, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Finds the test methods with a given fully qualified name.
    /// </summary>
    /// <param name="fullyQualifiedName">Name to look for.</param>
    /// <returns>The matching method nodes.</returns>
    public List<TestMethodNode> FindMethods(string fullyQualifiedName)
    {
        return Classes
            .SelectMany(static classNode => classNode.Methods)
            .Where(methodNode => IsSameMethod(methodNode, fullyQualifiedName))
            .ToList();
    }

    /// <summary>
    /// Finds the classes declaring a given test method.
    /// </summary>
    /// <param name="fullyQualifiedName">Name of the method to look for.</param>
    /// <returns>The class nodes that declare it.</returns>
    public List<TestClassNode> FindClassesContaining(string fullyQualifiedName)
    {
        return Classes
            .Where(classNode => classNode.Methods.Any(methodNode => IsSameMethod(methodNode, fullyQualifiedName)))
            .ToList();
    }

    /// <summary>
    /// Marks nodes as part of the run that is starting.
    /// </summary>
    /// <param name="methods">Methods being run.</param>
    /// <param name="classes">Classes containing those methods.</param>
    public static void MarkRunning(IEnumerable<TestMethodNode> methods, IEnumerable<TestClassNode> classes)
    {
        SetRunState(methods, classes, TestRunState.Running);
    }

    /// <summary>
    /// Clears the run state of the given nodes back to "not run". Used when a run is stopped,
    /// because the results of an interrupted run are unknown and must not be shown as
    /// failures.
    /// </summary>
    /// <param name="methods">Methods that were being run.</param>
    /// <param name="classes">Classes containing those methods.</param>
    public static void ResetRunStates(IEnumerable<TestMethodNode> methods, IEnumerable<TestClassNode> classes)
    {
        foreach (var methodNode in methods)
        {
            methodNode.RunState = TestRunState.None;
        }

        UpdateClassStates(classes);
    }

    /// <summary>
    /// Applies the result of a completed run to each test individually.
    /// </summary>
    /// <param name="methods">Methods that were run.</param>
    /// <param name="failedReportedNames">Raw names reported as failed in the output.</param>
    /// <param name="exitCode">Exit code of the run.</param>
    public static void ApplyResults(
        IEnumerable<TestMethodNode> methods,
        IReadOnlyCollection<string> failedReportedNames,
        int exitCode)
    {
        // dotnet test reports a non-zero exit code when any test fails, so the exit code alone
        // cannot decide individual results. It is only authoritative when everything passed,
        // or when no per-test failure could be parsed out of the output.
        if (exitCode == 0)
        {
            SetRunState(methods, [], TestRunState.Passed);
            return;
        }

        if (failedReportedNames.Count == 0)
        {
            SetRunState(methods, [], TestRunState.Failed);
            return;
        }

        foreach (var methodNode in methods)
        {
            methodNode.RunState = IsReportedAsFailed(methodNode, failedReportedNames)
                ? TestRunState.Failed
                : TestRunState.Passed;
        }
    }

    /// <summary>
    /// Recomputes the run state of each class from the states of its methods.
    /// </summary>
    /// <param name="classes">Classes to update.</param>
    public static void UpdateClassStates(IEnumerable<TestClassNode> classes)
    {
        foreach (var classNode in classes)
        {
            classNode.UpdateRunStateFromMethods();
        }
    }

    /// <summary>
    /// Determines whether a test was reported as failed, allowing for the name variations
    /// used by the different test frameworks.
    /// </summary>
    /// <param name="methodNode">Test to check.</param>
    /// <param name="failedReportedNames">Raw names reported as failed.</param>
    /// <returns><see langword="true"/> when the test failed.</returns>
    public static bool IsReportedAsFailed(TestMethodNode methodNode, IEnumerable<string> failedReportedNames)
    {
        return failedReportedNames.Any(reportedName => TestNameMatcher.Matches(
            reportedName,
            methodNode.FullyQualifiedName,
            methodNode.MethodName));
    }

    private static void SetRunState(
        IEnumerable<TestMethodNode> methods,
        IEnumerable<TestClassNode> classes,
        TestRunState runState)
    {
        foreach (var methodNode in methods)
        {
            methodNode.RunState = runState;
        }

        foreach (var classNode in classes)
        {
            classNode.RunState = runState;
        }
    }

    private static bool IsSameMethod(TestMethodNode methodNode, string fullyQualifiedName)
    {
        return string.Equals(methodNode.FullyQualifiedName, fullyQualifiedName, StringComparison.OrdinalIgnoreCase);
    }

    private void OnNodeSelectionChanged(object? sender, EventArgs e)
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
