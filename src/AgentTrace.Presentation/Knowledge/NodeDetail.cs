using System.Diagnostics;
using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Presentation.Resources;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Core.Knowledge.KnowledgeGraph;

namespace AgentTrace.Presentation.Knowledge;

/// <summary>
/// What the panel of a graph file shows, already as text. It is a value: if it does not change between two
/// updates, the panel is not rebuilt. No Avalonia.
/// <c>Related</c>: the related files, from most to least weight (at most <see cref="MaxRelated"/>).
/// </summary>
public sealed record NodeDetail(FilePath Path, string Title, ValueList<NodeDetail.Field> Fields, ValueList<NodeDetail.RelatedFile> Related, bool Changed)
{
    public const int MaxRelated = 12;

    public sealed record Field(string Label, string Value);

    public sealed record RelatedFile(FilePath Path, string Relation, int Weight);

    public static NodeDetail? Of(KnowledgeGraph graph, FilePath path, FilePath? projectRoot)
    {
        Node? n = graph.FindNode(path);
        if (n is null) return null;
        var fields = new List<Field>
        {
            new(MapStrings.NodeFieldState, State(n)),
            new(MapStrings.NodeFieldReads, Count(n.Reads)),
        };
        if (n.Created > 0) fields.Add(new Field(MapStrings.NodeFieldCreations, Count(n.Created)));
        if (n.Modified > 0) fields.Add(new Field(MapStrings.NodeFieldModifications, Count(n.Modified)));
        if (n.Deleted > 0) fields.Add(new Field(MapStrings.NodeFieldDeletions, Count(n.Deleted)));
        if (n.External > 0) fields.Add(new Field(MapStrings.NodeFieldExternalChanges, string.Format(CultureInfo.CurrentCulture, MapStrings.NodeExternalChangesFormat, Count(n.External))));
        if (n.Errors > 0) fields.Add(new Field(MapStrings.NodeFieldErrors, Count(n.Errors)));
        fields.Add(new Field(MapStrings.NodeFieldTurns, string.Format(CultureInfo.CurrentCulture, MapStrings.NodeTurnsOfFormat, Count(n.TurnsTouched), Count(graph.Turns))));
        var related = new List<RelatedFile>();
        foreach (Edge e in graph.EdgesOf(path))
        {
            if (related.Count == MaxRelated) break;
            bool outgoing = e.From == path;
            FilePath other = outgoing ? e.To : e.From;
            related.Add(new RelatedFile(other, Describe(e.Relation, outgoing), e.Weight));
        }
        return new NodeDetail(path, RelativeTo(path, projectRoot), fields.ToValueList(), related.ToValueList(), n.Changed);
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    internal static string State(Node n)
    {
        string baseText = n.LastChange switch
        {
            null => n.Weight == 0 && n.Errors > 0 ? MapStrings.NodeStateOnlyErrors : MapStrings.NodeStateOnlyRead,
            ChangeKind.Created => MapStrings.NodeStateCreated,
            ChangeKind.Modified => MapStrings.NodeStateModified,
            ChangeKind.Deleted => MapStrings.NodeStateDeleted,
            _ => throw new UnreachableException(),
        };
        return n.OnlyExternal ? string.Format(CultureInfo.CurrentCulture, MapStrings.NodeStateExternalFormat, baseText) : baseText;
    }

    /// <summary>The relation seen from the selected file.</summary>
    internal static string Describe(Relation relation, bool outgoing) => relation switch
    {
        Relation.CoChanged => MapStrings.RelationCoChanged,
        Relation.ReadBeforeEdit => outgoing ? MapStrings.RelationReadBeforeEditOutgoing : MapStrings.RelationReadBeforeEditIncoming,
        _ => throw new UnreachableException(),
    };

    /// <summary>Path relative to the project if it is inside it; otherwise as is.</summary>
    public static string RelativeTo(FilePath path, FilePath? projectRoot)
    {
        if (projectRoot is { } root && path.IsUnder(root) && path != root) return path.RelativeTo(root);
        return path.ToString();
    }
}
