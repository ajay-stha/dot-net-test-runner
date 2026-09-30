namespace DotNetTestRunner.Application.Abstractions;

/// <summary>
/// Inspects the live Windows process tree. Used to observe the applications a test run
/// starts, so a run can be detected as abandoned when the application under test disappears.
/// </summary>
public interface IProcessTreeInspector
{
    /// <summary>
    /// Counts the processes descended from <paramref name="rootProcessId"/> that own a visible
    /// top-level window.
    /// </summary>
    /// <param name="rootProcessId">The process whose descendants are examined.</param>
    /// <returns>
    /// The number of descendant processes currently showing a window. Returns <c>0</c> when
    /// the tree cannot be inspected, so callers treat inspection failures as "nothing visible".
    /// </returns>
    int CountDescendantsWithVisibleWindow(int rootProcessId);
}
