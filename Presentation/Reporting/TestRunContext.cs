namespace DotNetTestRunner.Presentation.Reporting;

/// <summary>
/// Identifies a test run for reporting. The target and configuration are captured when the
/// run starts so later log records describe what actually ran, even if the user changes the
/// selection while the run is in progress.
/// </summary>
/// <param name="Header">Human-readable description of the run.</param>
/// <param name="TargetPath">Target the run used.</param>
/// <param name="Configuration">Build configuration the run used.</param>
public sealed record TestRunContext(string Header, string TargetPath, string Configuration);
