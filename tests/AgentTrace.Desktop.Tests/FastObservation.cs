using System.Runtime.CompilerServices;
using AgentTrace.Presentation.Observation;

namespace AgentTrace.Desktop.Tests;

/// <summary>The production timings are spaced out to spare the disk; the headless tests wait for refreshes, so they use short ones.</summary>
internal static class FastObservation
{
    [ModuleInitializer]
    internal static void Initialize() => MainWindow.Timing = new ObservationTiming(
        Debounce: TimeSpan.FromMilliseconds(300),
        MaxWait: TimeSpan.FromSeconds(2),
        TracePoll: TimeSpan.FromMilliseconds(500),
        TraceDiscovery: TimeSpan.FromSeconds(2),
        DiskRefresh: TimeSpan.FromMilliseconds(300));
}
