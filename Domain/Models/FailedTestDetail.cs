namespace DotNetTestRunner.Domain.Models;

/// <summary>
/// Identifies a single failed test together with the failure cause extracted from the
/// <c>dotnet test</c> console output, when available.
/// </summary>
public sealed class FailedTestDetail
{
    public FailedTestDetail(string fullyQualifiedName, string? failureReason)
    {
        FullyQualifiedName = fullyQualifiedName;
        FailureReason = failureReason;
    }

    /// <summary>
    /// Gets the fully qualified name of the failed test.
    /// </summary>
    public string FullyQualifiedName { get; }

    /// <summary>
    /// Gets the error message reported by the test framework, or <see langword="null"/> when
    /// it could not be extracted from the captured output.
    /// </summary>
    public string? FailureReason { get; }
}
