namespace DotNetTestRunner.Presentation.ViewModels;

/// <summary>
/// A single test method shown in the test tree.
/// </summary>
public sealed class TestMethodNode : ObservableObject
{
    private TestRunState _RunState;
    private bool _IsSelected;

    public TestMethodNode(string methodName, string fullyQualifiedName)
    {
        MethodName = methodName;
        FullyQualifiedName = fullyQualifiedName;
    }

    /// <summary>
    /// Gets the name of the test method.
    /// </summary>
    public string MethodName { get; }

    /// <summary>
    /// Gets the name used to filter this test when running it.
    /// </summary>
    public string FullyQualifiedName { get; }

    /// <summary>
    /// Gets or sets the class node this method belongs to, used to keep the parent's
    /// tri-state selection in sync.
    /// </summary>
    public TestClassNode? Owner { get; set; }

    /// <summary>
    /// Gets or sets the state shown for this test.
    /// </summary>
    public TestRunState RunState
    {
        get => _RunState;
        set => SetProperty(ref _RunState, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this test is selected to be run.
    /// </summary>
    public bool IsSelected
    {
        get => _IsSelected;
        set
        {
            if (SetProperty(ref _IsSelected, value))
            {
                Owner?.NotifyMethodSelectionChanged();
            }
        }
    }
}
