namespace DotNetTestRunner.Domain.Models;

/// <summary>
/// Identifies a single failed test together with the error and stack trace extracted from
/// the <c>dotnet test</c> console output, when available.
/// </summary>
public sealed class FailedTestDetail
{
    public FailedTestDetail(string fullyQualifiedName, string? errorMessage, string? stackTrace)
    {
        FullyQualifiedName = fullyQualifiedName;
        ErrorMessage = errorMessage;
        StackTrace = stackTrace;
    }

    /// <summary>
    /// Gets the fully qualified name of the failed test.
    /// </summary>
    public string FullyQualifiedName { get; }

    /// <summary>
    /// Gets the error message reported by the test framework, or <see langword="null"/> if it
    /// could not be extracted from the captured output.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Gets the stack trace reported by the test framework, or <see langword="null"/> if it
    /// could not be extracted from the captured output.
    /// </summary>
    public string? StackTrace { get; }
}
