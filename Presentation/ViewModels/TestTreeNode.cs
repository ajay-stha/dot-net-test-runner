namespace DotNetTestRunner.Presentation.ViewModels;

/// <summary>
/// Bindable expansion state shared by test explorer nodes.
/// </summary>
public abstract class TestTreeNode : ObservableObject
{
    private bool _IsExpanded;

    /// <summary>
    /// Gets or sets whether the node's children are visible.
    /// </summary>
    public bool IsExpanded
    {
        get => _IsExpanded;
        set => SetProperty(ref _IsExpanded, value);
    }
}
