using System.Diagnostics;

namespace AgentTrace.Presentation.Text;

/// <summary>
/// A row of the diff as shown: header, hunk (@@), context line, added or deleted line, or a message.
/// <c>OldLine</c>: line number in the previous version, or null if it does not exist there. <c>NewLine</c>: line number in the
/// current version, or null if it does not exist there. <c>Text</c>: content of the line, without the +/−/space prefix.
/// </summary>
public sealed record DiffRow(DiffRow.RowKind Kind, int? OldLine, int? NewLine, string Text)
{
    public enum RowKind { Header, Hunk, Context, Add, Del, Message }

    public static DiffRow Header(string text) => new(RowKind.Header, null, null, text);

    public static DiffRow Message(string text) => new(RowKind.Message, null, null, text);

    /// <summary>Is a content line of the file (not a header or a message).</summary>
    public bool IsCode => Kind is RowKind.Context or RowKind.Add or RowKind.Del;

    /// <summary>Text with the classic unified diff prefix: kept on screen so it does not depend on color.</summary>
    public string DisplayText => Kind switch
    {
        RowKind.Context => " " + Text,
        RowKind.Add => "+" + Text,
        RowKind.Del => "-" + Text,
        RowKind.Header or RowKind.Hunk or RowKind.Message => Text,
        _ => throw new UnreachableException(),
    };
}
