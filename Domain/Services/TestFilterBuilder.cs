namespace DotNetTestRunner.Domain.Services;

/// <summary>
/// Builds the <c>--filter</c> expressions passed to <c>dotnet test</c>.
/// </summary>
public static class TestFilterBuilder
{
    /// <summary>
    /// Builds a filter matching exactly the given tests.
    /// </summary>
    /// <param name="fullyQualifiedNames">Names of the tests to run.</param>
    /// <returns>A filter expression, or an empty string when no names were given.</returns>
    public static string ForExactTests(IEnumerable<string> fullyQualifiedNames)
    {
        return Join(fullyQualifiedNames, "FullyQualifiedName=");
    }

    /// <summary>
    /// Builds a filter matching the given tests and any parameterized cases derived from
    /// them. Data-driven tests are reported with an argument suffix, so a prefix match is
    /// needed to run every case of a class.
    /// </summary>
    /// <param name="fullyQualifiedNames">Names of the tests to run.</param>
    /// <returns>A filter expression, or an empty string when no names were given.</returns>
    public static string ForTestsAndTheirCases(IEnumerable<string> fullyQualifiedNames)
    {
        return Join(fullyQualifiedNames, "FullyQualifiedName~");
    }

    private static string Join(IEnumerable<string> fullyQualifiedNames, string clausePrefix)
    {
        var clauses = fullyQualifiedNames
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => string.Concat(clausePrefix, Escape(name)));

        return string.Join("|", clauses);
    }

    /// <summary>
    /// Escapes a value so it survives being embedded in a quoted command-line argument.
    /// </summary>
    /// <param name="value">Value to escape.</param>
    /// <returns>The escaped value.</returns>
    private static string Escape(string value)
    {
        return value.Replace("\"", "\\\"");
    }
}
