using AgentTrace.Core;

namespace AgentTrace.Presentation.Text;

/// <summary>A row of the telemetry list. <c>Path</c>: file the event refers to (change or read), or null: double-clicking it opens it.</summary>
public sealed record TelemetryRow(string Text, FilePath? Path)
{
    public static TelemetryRow Plain(string text) => new(text, null);

    public override string ToString() => Text;
}
