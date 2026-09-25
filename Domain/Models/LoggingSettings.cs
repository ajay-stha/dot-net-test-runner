namespace DotNetTestRunner.Domain.Models;

/// <summary>
/// Serializable configuration controlling log verbosity and rolling file behavior. Values are
/// read from <c>logsettings.json</c> deployed alongside the application executable.
/// </summary>
public sealed class LoggingSettings
{
    /// <summary>
    /// Gets or sets the minimum <c>Serilog.Events.LogEventLevel</c> name written to the log
    /// file (e.g. "Verbose", "Debug", "Information", "Warning", "Error", "Fatal").
    /// </summary>
    public string MinimumLevel { get; set; } = "Information";

    /// <summary>
    /// Gets or sets the <c>Serilog.RollingInterval</c> name controlling how often a new log
    /// file is created (e.g. "Day", "Hour", "Infinite").
    /// </summary>
    public string RollingInterval { get; set; } = "Day";

    /// <summary>
    /// Gets or sets the maximum number of rolled log files retained on disk, or
    /// <see langword="null"/> to keep all of them.
    /// </summary>
    public int? RetainedFileCountLimit { get; set; } = 31;

    /// <summary>
    /// Gets or sets the maximum size, in bytes, a log file can reach before rolling to a new
    /// file, or <see langword="null"/> for no size-based rolling.
    /// </summary>
    public long? FileSizeLimitBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Gets or sets whether the log file rolls over when <see cref="FileSizeLimitBytes"/> is
    /// reached, in addition to rolling on <see cref="RollingInterval"/>.
    /// </summary>
    public bool RollOnFileSizeLimit { get; set; } = true;
}
