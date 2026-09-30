namespace DotNetTestRunner.Domain.Models;

/// <summary>
/// The error message and stack trace extracted from the console output of a failed test.
/// Either part is <see langword="null"/> when it was not present in the captured output.
/// </summary>
/// <param name="ErrorMessage">Error message reported by the test framework.</param>
/// <param name="StackTrace">Stack trace reported by the test framework.</param>
public sealed record TestFailureDetail(string? ErrorMessage, string? StackTrace)
{
    /// <summary>
    /// A detail with neither an error message nor a stack trace.
    /// </summary>
    public static TestFailureDetail Empty { get; } = new(null, null);

    /// <summary>
    /// Gets a value indicating whether any part of the failure was captured.
    /// </summary>
    public bool HasContent => ErrorMessage is not null || StackTrace is not null;
}
