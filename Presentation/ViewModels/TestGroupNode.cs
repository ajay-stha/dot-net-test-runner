using System.ComponentModel;

namespace DotNetTestRunner.Presentation.ViewModels;

/// <summary>
/// A target or namespace grouping with selection and status aggregated from its classes.
/// </summary>
public sealed class TestGroupNode : TestTreeNode
{
    #region Fields

    private readonly IReadOnlyList<TestClassNode> _Classes;
    private bool _IsUpdatingSelection;

    #endregion

    #region Construction

    /// <summary>
    /// Creates a group containing class or group nodes.
    /// </summary>
    public TestGroupNode(string displayName, IReadOnlyList<TestTreeNode> children)
    {
        DisplayName = displayName;
        Children = children;
        IsExpanded = true;
        _Classes = children.SelectMany(child => child switch
        {
            TestClassNode classNode => new[] { classNode },
            TestGroupNode groupNode => groupNode._Classes,
            _ => throw new ArgumentException("Groups can contain only classes or groups.", nameof(children))
        }).ToList();

        foreach (var classNode in _Classes)
        {
            classNode.PropertyChanged += OnClassPropertyChanged;
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the target name or full namespace shown in the tree.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the immediate child nodes.
    /// </summary>
    public IReadOnlyList<TestTreeNode> Children { get; }

    /// <summary>
    /// Gets the number of descendant tests.
    /// </summary>
    public int TestCount => _Classes.Sum(static classNode => classNode.TestCount);

    /// <summary>
    /// Gets or sets the selection of every test in this group.
    /// </summary>
    public bool? IsSelected
    {
        get
        {
            if (_Classes.All(static classNode => classNode.IsSelected == false))
            {
                return false;
            }

            return _Classes.All(static classNode => classNode.IsSelected == true) ? true : null;
        }
        set
        {
            _IsUpdatingSelection = true;

            try
            {
                foreach (var classNode in _Classes)
                {
                    classNode.IsSelected = value ?? false;
                }
            }
            finally
            {
                _IsUpdatingSelection = false;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the aggregate status, prioritizing running tests and then failures.
    /// </summary>
    public TestRunState RunState
    {
        get
        {
            if (_Classes.Any(static classNode => classNode.RunState == TestRunState.Running))
            {
                return TestRunState.Running;
            }

            if (_Classes.Any(static classNode => classNode.RunState == TestRunState.Failed))
            {
                return TestRunState.Failed;
            }

            return _Classes.Any(static classNode => classNode.RunState == TestRunState.Passed)
                ? TestRunState.Passed
                : TestRunState.None;
        }
    }

    #endregion

    #region Notifications

    private void OnClassPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TestClassNode.IsSelected) && !_IsUpdatingSelection)
        {
            OnPropertyChanged(nameof(IsSelected));
        }
        else if (e.PropertyName == nameof(TestClassNode.RunState))
        {
            OnPropertyChanged(nameof(RunState));
        }
        else if (e.PropertyName == nameof(TestClassNode.TestCount))
        {
            OnPropertyChanged(nameof(TestCount));
        }
    }

    #endregion
}
