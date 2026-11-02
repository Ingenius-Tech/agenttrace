using System.Diagnostics;
using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Resources;
using AgentTrace.Presentation.Tree;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Text;

/// <summary>
/// How a tree entry is decorated according to what has happened to it. Does not depend on Avalonia: the cell only translates
/// this to styles, so the logic (which color, which badge, what the tooltip says) is tested without UI.
/// <c>Kind</c>: kind of change that rules the entry (in a folder, the strongest of what it contains), or null.
/// <c>Actor</c>: who did it; in a folder, External if any change is not attributed to the agent; null if there is no change.
/// <c>Count</c>: changed files (1 for a file). <c>Strike</c>: shown struck through: it is a deleted file (or folder)
/// that is no longer on disk. <c>Read</c>: the agent read it in the session. <c>Tooltip</c>: explanatory text, or null.
/// </summary>
public sealed record NodeDecoration(ChangeKind? Kind, Actor? Actor, int Count, bool Strike, bool Read, string? Tooltip)
{
    public static readonly NodeDecoration None = new(null, null, 0, false, false, null);

    public bool HasChange => Kind is not null;

    /// <summary>Text of the badge: the symbol and, in a folder, how many changes it contains.</summary>
    public string Badge
    {
        get
        {
            if (Kind is not { } kind) return "";
            return Count > 1 ? ChangeStyle.Symbol(kind) + " " + Count : ChangeStyle.Symbol(kind);
        }
    }

    public static NodeDecoration Of(FileNode node, FileStatusIndex index) => node.Directory ? OfFolder(node, index) : OfFile(node, index);

    private static NodeDecoration OfFile(FileNode node, FileStatusIndex index)
    {
        FileStatus? s = index.File(node.Path);
        if (s is null) return None;
        if (!s.Changed) return new NodeDecoration(null, null, 0, false, s.Read, TextStrings.NodeReadByAgentTooltip);
        ChangeKind kind = s.Kind!.Value;
        var parts = new List<string> { ChangeStyle.Label(kind) + " · " + ActorText(s.Actor) };
        if (s.Read) parts.Add(TextStrings.NodeReadByAgentPart);
        return new NodeDecoration(kind, s.Actor, 1, kind == ChangeKind.Deleted && node.Ghost, s.Read, Capitalize(string.Join(" · ", parts)));
    }

    private static NodeDecoration OfFolder(FileNode node, FileStatusIndex index)
    {
        FolderSummary summary = index.Folder(node.Path);
        if (summary.Total == 0) return None;
        Actor actor = summary.External > 0 ? Core.Actor.External : Core.Actor.Agent;
        var counts = new List<string>();
        if (summary.Created > 0) counts.Add(summary.Created + " " + ChangeStyle.PluralLabel(ChangeKind.Created));
        if (summary.Modified > 0) counts.Add(summary.Modified + " " + ChangeStyle.PluralLabel(ChangeKind.Modified));
        if (summary.Deleted > 0) counts.Add(summary.Deleted + " " + ChangeStyle.PluralLabel(ChangeKind.Deleted));
        string external = summary.External > 0
            ? string.Format(CultureInfo.CurrentCulture, Plural.Pick(summary.External, TextStrings.FolderExternal_One, TextStrings.FolderExternal_Other), summary.External)
            : "";
        string tooltip = string.Format(CultureInfo.CurrentCulture, Plural.Pick(summary.Total, TextStrings.FolderChanges_One, TextStrings.FolderChanges_Other),
            summary.Total, string.Join(", ", counts), external);
        return new NodeDecoration(summary.Strongest(), actor, summary.Total, node.Ghost, false, tooltip);
    }

    private static string ActorText(Actor actor) => actor switch
    {
        Core.Actor.Agent => TextStrings.ActorAgent,
        Core.Actor.External => TextStrings.ActorExternal,
        Core.Actor.Unknown => TextStrings.ActorUnknown,
        _ => throw new UnreachableException(),
    };

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
