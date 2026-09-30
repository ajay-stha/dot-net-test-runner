using System.Windows.Threading;
using DotNetTestRunner.Domain.Models;
using DotNetTestRunner.Domain.Services;

namespace DotNetTestRunner.Presentation.ViewModels;

/// <summary>
/// Follows a run as its output arrives, moving each test out of the "running" state as soon
/// as its result is reported instead of waiting for the whole run to finish. Results are
/// read on the process output threads, so they are applied to the tree through the UI
/// dispatcher.
/// </summary>
public sealed class TestRunProgressTracker
{
    private readonly Dispatcher _UiDispatcher;
    private readonly IReadOnlyList<TestMethodNode> _Methods;
    private readonly IReadOnlyList<TestClassNode> _Classes;
    private readonly Action<TestRunProgress> _OnProgress;
    private readonly HashSet<TestMethodNode> _CompletedMethods = [];

    public TestRunProgressTracker(
        Dispatcher uiDispatcher,
        IReadOnlyList<TestMethodNode> methods,
        IReadOnlyList<TestClassNode> classes,
        Action<TestRunProgress> onProgress)
    {
        _UiDispatcher = uiDispatcher;
        _Methods = methods;
        _Classes = classes;
        _OnProgress = onProgress;
    }

    /// <summary>
    /// Records a reported result. Safe to call from any thread.
    /// </summary>
    /// <param name="notice">The result read from the run's output.</param>
    public void Report(TestResultNotice notice)
    {
        _ = _UiDispatcher.BeginInvoke(() => Apply(notice));
    }

    /// <summary>
    /// Applies a reported result to every test it names and reports how far the run has got.
    /// </summary>
    /// <param name="notice">The result read from the run's output.</param>
    private void Apply(TestResultNotice notice)
    {
        var matchedAny = false;

        foreach (var methodNode in _Methods)
        {
            if (!TestNameMatcher.Matches(notice.ReportedName, methodNode.FullyQualifiedName, methodNode.MethodName))
            {
                continue;
            }

            matchedAny = true;
            _CompletedMethods.Add(methodNode);

            // A data-driven test reports one result per case, so a later passing case must
            // not clear a failure already reported for the same test.
            if (methodNode.RunState != TestRunState.Failed)
            {
                methodNode.RunState = ToRunState(notice.Outcome);
            }
        }

        if (!matchedAny)
        {
            return;
        }

        TestTreeViewModel.UpdateClassStates(_Classes);
        _OnProgress(new TestRunProgress(
            notice.ReportedName,
            notice.Outcome,
            _CompletedMethods.Count,
            _Methods.Count));
    }

    private static TestRunState ToRunState(TestOutcome outcome)
    {
        return outcome switch
        {
            TestOutcome.Passed => TestRunState.Passed,
            TestOutcome.Failed => TestRunState.Failed,
            _ => TestRunState.None
        };
    }
}
