using System.Diagnostics;
using System.Globalization;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Text;
using static AgentTrace.Core.Flow.FlowGraph;

namespace AgentTrace.Presentation.Graph;

/// <summary>Graph texts that do not depend on Avalonia (so they can be tested).</summary>
public static class StepText
{
    /// <summary>"850 ms", "4.9 s" ("4,9 s" in Spanish), "1 min 5 s", "1 h 2 min".</summary>
    public static string FormatDuration(TimeSpan? d)
    {
        if (d is null) return "";
        long ms = Math.Max((long)Math.Floor(d.Value.TotalMilliseconds), 0);
        if (ms < 1000) return ms + " ms";
        // Java: String.format("%.1f", ms / 1000.0): rounds half up, with the separator of the current culture
        if (ms < 60_000) return LocalNumbers.OneDecimal(ms, 1000) + " s";
        long seconds = ms / 1000;
        if (seconds < 3600) return seconds / 60 + " min " + seconds % 60 + " s";
        return seconds / 3600 + " h " + seconds % 3600 / 60 + " min";
    }

    /// <summary>The state of the step without relying on color: "running · 12.0 s", "error · 4.9 s", "4.9 s", "no result".</summary>
    public static string Status(Step step, DateTimeOffset now) => step.State switch
    {
        StepState.Running => step.Start is null ? GraphStrings.StepStatusRunning
            : string.Format(CultureInfo.CurrentCulture, GraphStrings.StepStatusRunningFormat, FormatDuration(now - step.Start.Value)),
        StepState.Error => step.Duration is null ? GraphStrings.StepStatusError
            : string.Format(CultureInfo.CurrentCulture, GraphStrings.StepStatusErrorFormat, FormatDuration(step.Duration)),
        StepState.Ok => step.Duration is null ? GraphStrings.StepStatusOk : FormatDuration(step.Duration),
        StepState.NoResult => GraphStrings.StepStatusNoResult,
        _ => throw new UnreachableException(),
    };
}
