using System.Collections.ObjectModel;

namespace DotNetTestRunner.Presentation.ViewModels;

/// <summary>
/// A test class shown in the test tree, grouping the test methods it declares.
/// </summary>
public sealed class TestClassNode : ObservableObject
{
    private TestRunState _RunState;
    private bool? _IsSelected = false;
    private bool _IsUpdatingSelection;

    public TestClassNode(string className)
    {
        ClassName = className;
    }

    /// <summary>
    /// Raised whenever the selection of this class or any of its methods changes.
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Gets the fully qualified name of the class.
    /// </summary>
    public string ClassName { get; }

    /// <summary>
    /// Gets the test methods declared by the class.
    /// </summary>
    public ObservableCollection<TestMethodNode> Methods { get; } = [];

    /// <summary>
    /// Gets or sets the state shown for this class.
    /// </summary>
    public TestRunState RunState
    {
        get => _RunState;
        set => SetProperty(ref _RunState, value);
    }

    /// <summary>
    /// Gets or sets the tri-state selection of the class. Setting it selects or clears every
    /// method; <see langword="null"/> means only some methods are selected and is produced by
    /// the node itself rather than assigned.
    /// </summary>
    public bool? IsSelected
    {
        get => _IsSelected;
        set => ApplySelectionToMethods(value ?? false);
    }

    /// <summary>
    /// Recomputes the class selection after one of its methods changed. Ignored while the
    /// class is itself applying a selection to its methods.
    /// </summary>
    public void NotifyMethodSelectionChanged()
    {
        if (_IsUpdatingSelection)
        {
            return;
        }

        RefreshSelectionState();
    }

    /// <summary>
    /// Recomputes the run state of the class from the states of its methods.
    /// </summary>
    public void UpdateRunStateFromMethods()
    {
        RunState = GetAggregateMethodState();
    }

    private TestRunState GetAggregateMethodState()
    {
        if (Methods.Any(static methodNode => methodNode.RunState == TestRunState.Running))
        {
            return TestRunState.Running;
        }

        if (Methods.Any(static methodNode => methodNode.RunState == TestRunState.Failed))
        {
            return TestRunState.Failed;
        }

        if (Methods.Any(static methodNode => methodNode.RunState == TestRunState.Passed))
        {
            return TestRunState.Passed;
        }

        return TestRunState.None;
    }

    private void ApplySelectionToMethods(bool isSelected)
    {
        _IsUpdatingSelection = true;

        try
        {
            foreach (var methodNode in Methods)
            {
                methodNode.IsSelected = isSelected;
            }
        }
        finally
        {
            _IsUpdatingSelection = false;
        }

        RefreshSelectionState();
    }

    private void RefreshSelectionState()
    {
        var newState = GetAggregateSelectionState();

        if (_IsSelected != newState)
        {
            _IsSelected = newState;
            OnPropertyChanged(nameof(IsSelected));
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool? GetAggregateSelectionState()
    {
        if (Methods.Count == 0 || Methods.All(static methodNode => !methodNode.IsSelected))
        {
            return false;
        }

        return Methods.All(static methodNode => methodNode.IsSelected) ? true : null;
    }
}
