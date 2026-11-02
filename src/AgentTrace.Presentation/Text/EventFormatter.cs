using System.Diagnostics;
using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Presentation.Resources;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Text;

/// <summary>
/// Text of one line to show events and summaries in the interface. Does not depend on Avalonia.
/// Seconds use the decimal separator of the current culture (en: <c>1.5 s</c>, es: <c>1,5 s</c>).
/// </summary>
public static class EventFormatter
{
    private const int MaxLength = 200;

    /// <summary>Line for the telemetry list, or null if the event is not shown (ignored lines).</summary>
    public static string? Line(AgentEvent evt)
    {
        string? body = evt switch
        {
            UserMessage e => F(TextStrings.EventUserFormat, JavaText.OneLine(e.Text, MaxLength)),
            AssistantText e => F(TextStrings.EventAssistantFormat, JavaText.OneLine(e.Text, MaxLength)),
            Thinking e => e.Duration is null ? TextStrings.EventThinking : F(TextStrings.EventThinkingDurationFormat, Seconds(e.Duration.Value)),
            ToolCall e => F(TextStrings.EventToolCallFormat, e.ToolName, JavaText.OneLine(e.InputJson, MaxLength)),
            ToolResult e => F(e.Error ? TextStrings.EventToolErrorFormat : TextStrings.EventToolResultFormat, JavaText.OneLine(e.Output, MaxLength)),
            FileChanged e => F(e.CallId is null ? TextStrings.EventFileChangedExternalFormat : TextStrings.EventFileChangedFormat, KindLabel(e.Kind), e.Path),
            FileRead e => F(TextStrings.EventFileReadFormat, e.Path),
            SubagentStarted e => F(TextStrings.EventSubagentFormat, JavaText.OneLine(e.Description, MaxLength)),
            TokenUsage e => F(TextStrings.EventTokensFormat, e.Input, e.Output, e.CacheRead + e.CacheCreation),
            TurnEnded e => e.Duration is null ? TextStrings.EventTurnEnded : F(TextStrings.EventTurnEndedDurationFormat, Seconds(e.Duration.Value)),
            Skipped => null,
            _ => throw new UnreachableException(),
        };
        if (body is null) return null;
        EventHeader h = evt.Header;
        string time = h.Timestamp is null ? "--:--:--" : Time(h.Timestamp.Value);
        string who = h.SubagentId is null ? "" : "↳ " + h.SubagentId + " · ";
        return time + "  " + who + body;
    }

    /// <summary>The file an event refers to (a change or a read), if any. Tool inputs are not parsed.</summary>
    public static FilePath? PathOf(AgentEvent evt) => evt switch
    {
        FileChanged e => e.Path,
        FileRead e => e.Path,
        _ => null,
    };

    public static string KindLabel(ChangeKind kind) => ChangeStyle.Label(kind);

    /// <summary>HH:mm:ss in the time zone of the machine (<c>ZoneId.systemDefault()</c>).</summary>
    private static string Time(DateTimeOffset timestamp) =>
        TimeZoneInfo.ConvertTime(timestamp, TimeZoneInfo.Local).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static string F(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static string Seconds(TimeSpan d) => LocalNumbers.OneDecimal((long)Math.Floor(d.TotalMilliseconds), 1000) + " s";
}
