using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Resources;

namespace AgentTrace.Presentation.Text;

/// <summary>
/// The counters of the status bar: what is shown, with which number and in which tone. No Avalonia, so it can be tested; the
/// bar only paints them. The tone follows the color code of the application (error red, created green, modified amber,
/// read blue) and is never the only thing that says something: each counter carries its label.
/// </summary>
public static class StatusSegments
{
    public enum SegmentKind { Events, Turns, Tools, Errors, Files, External, Subagents, Tokens }

    public enum Tone { Neutral, Info, Ok, Warn, Error }

    public sealed record Segment(SegmentKind Kind, string Label, string Value, Tone Tone, string Tooltip);

    public static IReadOnlyList<Segment> Of(SessionStats s)
    {
        var output = new List<Segment>
        {
            new(SegmentKind.Events, TextStrings.StatusEventsLabel, Compact(s.Events), Tone.Neutral, string.Format(CultureInfo.CurrentCulture, TextStrings.StatusEventsTooltipFormat, Full(s.Events))),
            new(SegmentKind.Turns, Plural.Pick(s.Turns, TextStrings.StatusTurnLabel_One, TextStrings.StatusTurnLabel_Other), s.Turns.ToString(), Tone.Neutral, TextStrings.StatusTurnsTooltip),
            new(SegmentKind.Tools, TextStrings.StatusToolsLabel, Compact(s.TotalToolCalls), Tone.Info, TextStrings.StatusToolsTooltip),
        };
        long errors = s.ToolErrors;
        output.Add(new Segment(SegmentKind.Errors, Plural.Pick(errors, TextStrings.StatusErrorLabel_One, TextStrings.StatusErrorLabel_Other), errors.ToString(), errors > 0 ? Tone.Error : Tone.Ok,
            errors > 0 ? TextStrings.StatusErrorsTooltip : TextStrings.StatusNoErrorsTooltip));
        output.Add(new Segment(SegmentKind.Files, TextStrings.StatusFilesLabel, s.FilesTouched.Count.ToString(), Tone.Warn, TextStrings.StatusFilesTooltip));
        if (s.FilesChangedExternally.Count > 0)
        {
            output.Add(new Segment(SegmentKind.External, TextStrings.StatusExternalLabel, s.FilesChangedExternally.Count.ToString(), Tone.Neutral,
                TextStrings.StatusExternalTooltip));
        }
        if (s.Subagents.Count > 0)
        {
            output.Add(new Segment(SegmentKind.Subagents, Plural.Pick(s.Subagents.Count, TextStrings.StatusSubagentLabel_One, TextStrings.StatusSubagentLabel_Other), s.Subagents.Count.ToString(),
                Tone.Info, TextStrings.StatusSubagentsTooltip));
        }
        output.Add(new Segment(SegmentKind.Tokens, TextStrings.StatusTokensLabel, Compact(s.InputTokens) + " → " + Compact(s.OutputTokens), Tone.Info,
            string.Format(CultureInfo.CurrentCulture, TextStrings.StatusTokensTooltipFormat,
                Full(s.InputTokens), Full(s.OutputTokens), Full(s.CacheReadTokens), Full(s.CacheCreationTokens))));
        return output;
    }

    /// <summary>950 → "950", 4 932 → "4.9 K" (es: "4,9 K"), 1 234 567 → "1.2 M".</summary>
    public static string Compact(long n)
    {
        if (n < 1_000) return n.ToString();
        if (n < 1_000_000) return LocalNumbers.OneDecimal(n, 1_000) + " K";
        if (n < 1_000_000_000) return LocalNumbers.OneDecimal(n, 1_000_000) + " M";
        return LocalNumbers.OneDecimal(n, 1_000_000_000) + " G";
    }

    /// <summary>With the group separator: 1 234 567 → "1,234,567" (es: "1.234.567").</summary>
    public static string Full(long n) => LocalNumbers.Grouped(n);
}
