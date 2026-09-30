namespace DotNetTestRunner.Domain.Services;

/// <summary>
/// Matches the raw test names printed by <c>dotnet test</c> against the tests the runner
/// knows about. Reported names vary by test framework and may carry a parameter suffix for
/// data-driven tests, so an exact comparison is not enough.
/// </summary>
public static class TestNameMatcher
{
    /// <summary>
    /// Determines whether a name reported in the console output refers to a known test.
    /// </summary>
    /// <param name="reportedName">Raw name taken from a "Failed"/"Passed" output line.</param>
    /// <param name="fullyQualifiedName">Fully qualified name of the known test.</param>
    /// <param name="methodName">Method name of the known test.</param>
    /// <returns><see langword="true"/> when the reported name refers to that test.</returns>
    public static bool Matches(string reportedName, string fullyQualifiedName, string methodName)
    {
        var candidate = StripParameterSuffix(reportedName);

        if (candidate.Length == 0)
        {
            return false;
        }

        return string.Equals(candidate, fullyQualifiedName, StringComparison.OrdinalIgnoreCase)
            || fullyQualifiedName.EndsWith("." + candidate, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, methodName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Removes the argument list a test framework appends to data-driven test names, so
    /// <c>Add_Works(1,2)</c> is compared as <c>Add_Works</c>.
    /// </summary>
    /// <param name="testName">Name to strip.</param>
    /// <returns>The name without its parameter suffix.</returns>
    public static string StripParameterSuffix(string testName)
    {
        var openParenthesisIndex = testName.IndexOf('(');
        var candidate = openParenthesisIndex >= 0
            ? testName[..openParenthesisIndex]
            : testName;

        return candidate.Trim();
    }
}
