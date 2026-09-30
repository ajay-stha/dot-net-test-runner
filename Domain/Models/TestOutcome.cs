namespace DotNetTestRunner.Domain.Models;

/// <summary>
/// The result a test framework reported for a single test.
/// </summary>
public enum TestOutcome
{
    /// <summary>
    /// The test passed.
    /// </summary>
    Passed,

    /// <summary>
    /// The test failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The test was skipped and produced no result.
    /// </summary>
    Skipped
}
