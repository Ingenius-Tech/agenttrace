namespace AgentTrace.Presentation.Observation;

/// <summary>
/// How often the observation looks at the disk. Every refresh of the changes walks the working tree with git, so the defaults are
/// spaced out to keep a busy machine (a build, an install, an agent writing a lot) from being slowed down by the viewer.
/// Tests pass shorter values.
/// </summary>
/// <param name="Debounce">Quiet time after a disk change before git is asked again.</param>
/// <param name="MaxWait">Longest wait since the first change: under continuous writing git is asked at most this often.</param>
/// <param name="TracePoll">How often the trace files are read.</param>
/// <param name="TraceDiscovery">How often the active session is looked up again.</param>
/// <param name="DiskRefresh">Minimum wait before the tree, the heatmap and the areas map list the project again.</param>
public sealed record ObservationTiming(
    TimeSpan Debounce,
    TimeSpan MaxWait,
    TimeSpan TracePoll,
    TimeSpan TraceDiscovery,
    TimeSpan DiskRefresh)
{
    public static ObservationTiming Default { get; } = new(
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
}
