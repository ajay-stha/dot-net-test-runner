namespace DotNetTestRunner.Domain.Models;

/// <summary>
/// How far a run has got, reported each time a test finishes.
/// </summary>
/// <param name="ReportedName">The test name as the test framework reported it.</param>
/// <param name="Outcome">The result reported for the test.</param>
/// <param name="CompletedCount">How many of the run's tests have finished.</param>
/// <param name="TotalCount">How many tests the run covers.</param>
public readonly record struct TestRunProgress(
    string ReportedName,
    TestOutcome Outcome,
    int CompletedCount,
    int TotalCount);
