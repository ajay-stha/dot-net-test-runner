namespace DotNetTestRunner.Domain.Models;

/// <summary>
/// Why an in-progress run ended before all of its tests had reported.
/// </summary>
public enum RunStopReason
{
    /// <summary>
    /// The run was not stopped early.
    /// </summary>
    None,

    /// <summary>
    /// The user requested that the run stop.
    /// </summary>
    User,

    /// <summary>
    /// Every application started by the run was closed, so it could not make progress.
    /// </summary>
    ApplicationClosed,

    /// <summary>
    /// The run exceeded the time allowed for it.
    /// </summary>
    TimedOut
}
