namespace DotNetTestRunner.Presentation.ViewModels;

/// <summary>
/// The state shown for a test or test class in the tree.
/// </summary>
public enum TestRunState
{
    /// <summary>
    /// The test has not been run, or the result of the last run is unknown.
    /// </summary>
    None,

    /// <summary>
    /// The test is part of the run currently in progress.
    /// </summary>
    Running,

    /// <summary>
    /// The test passed in the last completed run.
    /// </summary>
    Passed,

    /// <summary>
    /// The test failed in the last completed run.
    /// </summary>
    Failed
}
