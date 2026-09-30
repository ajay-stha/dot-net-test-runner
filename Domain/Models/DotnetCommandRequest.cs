namespace DotNetTestRunner.Domain.Models;

/// <summary>
/// Describes a single <c>dotnet</c> invocation made on behalf of the user.
/// </summary>
public sealed record DotnetCommandRequest
{
    /// <summary>
    /// Gets the arguments passed to <c>dotnet</c>, without the executable name.
    /// </summary>
    public required string Arguments { get; init; }

    /// <summary>
    /// Gets the directory the command runs in.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Gets how long the command may run before it is terminated, or <see langword="null"/>
    /// to let it run until it exits or is cancelled.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Gets a value indicating whether the run should be stopped automatically once every
    /// application started by the command has been closed. Enable this for test runs that
    /// drive a desktop application; leave it disabled for discovery and build commands.
    /// </summary>
    public bool WatchForClosedAppUnderTest { get; init; }
}
