using AgentTrace.Core;
using AgentTrace.Presentation.Areas;

namespace AgentTrace.Presentation.Panorama;

/// <summary>The pane a focus comes from (it never receives its own focus back).</summary>
public enum FocusOrigin { Graph, Areas, Knowledge }

/// <summary>What was selected: a step of the graph, a turn, a task, a cell of the Areas map or a file of Knowledge.</summary>
public enum FocusKind { Step, Turn, Task, AreaCell, File }

/// <summary>
/// A selection made by the user in one pane, in neutral terms. Turn indices are those of the flow everywhere (the Areas map already uses them).
/// Which fields are set depends on the kind: <c>CallId</c> (Step), <c>Turn</c> (Turn), <c>Task</c> (Task), <c>Area</c> + <c>Turn</c> (AreaCell), <c>File</c> (File).
/// </summary>
public sealed record TraceFocus(FocusOrigin Origin, FocusKind Kind, string? CallId = null, int? Turn = null, int? Task = null,
    AreaRowKey? Area = null, FilePath? File = null)
{
    public static TraceFocus ForStep(string callId) => new(FocusOrigin.Graph, FocusKind.Step, CallId: callId);

    public static TraceFocus ForGraphTurn(int turn) => new(FocusOrigin.Graph, FocusKind.Turn, Turn: turn);

    public static TraceFocus ForAreasTurn(int turn) => new(FocusOrigin.Areas, FocusKind.Turn, Turn: turn);

    public static TraceFocus ForTask(int task) => new(FocusOrigin.Areas, FocusKind.Task, Task: task);

    public static TraceFocus ForCell(AreaRowKey area, int turn) => new(FocusOrigin.Areas, FocusKind.AreaCell, Turn: turn, Area: area);

    public static TraceFocus ForFile(FilePath file) => new(FocusOrigin.Knowledge, FocusKind.File, File: file);
}

/// <summary>
/// What the other panes highlight for a focus. <c>FirstTurn</c>..<c>LastTurn</c>: the turns involved (null when unknown); <c>CallIds</c>: the steps;
/// <c>Files</c>: the files; <c>AreaRows</c>: the rows of the Areas map holding those files. An empty set means "nothing to dim" for that pane.
/// </summary>
public sealed record FocusTargets(int? FirstTurn, int? LastTurn, IReadOnlySet<string> CallIds, IReadOnlySet<FilePath> Files, IReadOnlySet<AreaRowKey> AreaRows)
{
    public static readonly FocusTargets Empty = new(null, null, new HashSet<string>(), new HashSet<FilePath>(), new HashSet<AreaRowKey>());

    public bool IsEmpty => FirstTurn is null && CallIds.Count == 0 && Files.Count == 0 && AreaRows.Count == 0;

    public bool HasTurn(int turn) => FirstTurn is { } first && LastTurn is { } last && turn >= first && turn <= last;
}
