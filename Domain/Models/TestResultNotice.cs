namespace DotNetTestRunner.Domain.Models;

/// <summary>
/// A single test result read from a line of <c>dotnet test</c> output while a run is still in
/// progress.
/// </summary>
/// <param name="ReportedName">The test name exactly as the test framework reported it.</param>
/// <param name="Outcome">The result reported for the test.</param>
public sealed record TestResultNotice(string ReportedName, TestOutcome Outcome);
