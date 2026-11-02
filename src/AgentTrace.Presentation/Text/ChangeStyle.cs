using System.Diagnostics;
using AgentTrace.Presentation.Resources;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Text;

/// <summary>The single table of symbol, style class and text per kind of change: shared by the tree, the change list and the telemetry.</summary>
public static class ChangeStyle
{
    /// <summary>One-character symbol: understandable without telling colors apart.</summary>
    public static string Symbol(ChangeKind kind) => kind switch
    {
        ChangeKind.Created => "+",
        ChangeKind.Modified => "~",
        ChangeKind.Deleted => "−",
        _ => throw new UnreachableException(),
    };

    /// <summary>Name of the style class (Java's <c>cssClass</c>) it is painted with.</summary>
    public static string StyleClass(ChangeKind kind) => kind switch
    {
        ChangeKind.Created => "status-created",
        ChangeKind.Modified => "status-modified",
        ChangeKind.Deleted => "status-deleted",
        _ => throw new UnreachableException(),
    };

    public static string Label(ChangeKind kind) => kind switch
    {
        ChangeKind.Created => TextStrings.ChangeLabelCreated,
        ChangeKind.Modified => TextStrings.ChangeLabelModified,
        ChangeKind.Deleted => TextStrings.ChangeLabelDeleted,
        _ => throw new UnreachableException(),
    };

    public static string PluralLabel(ChangeKind kind) => kind switch
    {
        ChangeKind.Created => TextStrings.ChangePluralCreated,
        ChangeKind.Modified => TextStrings.ChangePluralModified,
        ChangeKind.Deleted => TextStrings.ChangePluralDeleted,
        _ => throw new UnreachableException(),
    };
}
