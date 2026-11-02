using AgentTrace.Core;
using AgentTrace.Core.Flow;
using static AgentTrace.Core.Flow.FlowGraph;
using static AgentTrace.Presentation.Graph.GraphGeometry;

namespace AgentTrace.Presentation.Graph;

/// <summary>
/// Lays out a <see cref="FlowGraph"/>. It thinks in two axes: the <em>long</em> one is sequential (one column per batch,
/// prompt or answer; each turn is a band) and the <em>cross</em> one spreads parallel calls and lanes: the main thread
/// first and the subagents after, sharing a row when they do not overlap on the long axis. The
/// <see cref="GraphOrientation"/> only decides whether the long axis is X (horizontal) or Y (vertical). Pure, deterministic function.
/// </summary>
public static class FlowLayout
{
    internal const int Margin = 24;

    /// <summary>Cross margin for the turn labels: top when horizontal, left when vertical.</summary>
    internal const int HeaderH = 36;

    public const int HeaderV = 170;
    internal const int RowGap = 28;
    internal static readonly TimeSpan LongGap = TimeSpan.FromSeconds(60);

    private static readonly IReadOnlySet<int> NoTurns = new HashSet<int>();

    /// <summary>The batches of a turn per lane, with the lanes in order of appearance (Java's <c>LinkedHashMap</c>).</summary>
    private sealed class TurnLanes
    {
        public readonly List<string> Order = [];
        public readonly Dictionary<string, List<int>> Batches = [];

        public bool Contains(string lane) => Batches.ContainsKey(lane);

        public void Add(string lane, int batchIndex)
        {
            if (!Batches.TryGetValue(lane, out List<int>? list))
            {
                Batches[lane] = list = [];
                Order.Add(lane);
            }
            list.Add(batchIndex);
        }
    }

    public static GraphGeometry Of(FlowGraph graph, IReadOnlySet<int>? collapsed = null, GraphOrientation orientation = GraphOrientation.Horizontal)
    {
        collapsed ??= NoTurns;
        bool v = orientation.IsVertical();
        int cardW = v ? VCardW : CardW;
        int alongExtent = v ? CardH : CardW;
        int alongStep = v ? RowStep : ColW;
        int crossStep = (v ? cardW : CardH) + Gap;
        int crossStart = v ? HeaderV : HeaderH;

        IReadOnlyList<Batch> batches = graph.Batches;
        int nTurns = Math.Max(graph.Turns.Count, 1); // a subagent can leave batches before any turn exists
        var byTurn = new Dictionary<int, TurnLanes>();
        var hidden = new bool[batches.Count];
        for (int i = 0; i < batches.Count; i++)
        {
            Batch b = batches[i];
            int t = Math.Min(b.TurnIndex, nTurns - 1);
            hidden[i] = collapsed.Contains(t);
            if (!byTurn.TryGetValue(t, out TurnLanes? lanesOfTurn)) byTurn[t] = lanesOfTurn = new TurnLanes();
            lanesOfTurn.Add(b.LaneId, i);
        }

        // 1. Columns (positions on the long axis).
        var batchCol = new int[batches.Count];
        var promptCol = new int[nTurns];
        var answerCol = new int[nTurns];
        var foldedCol = new int[nTurns];
        var bandFirst = new int[nTurns];
        var bandLast = new int[nTurns];
        int col = 0;
        for (int t = 0; t < nTurns; t++)
        {
            Turn? turn = t < graph.Turns.Count ? graph.Turns[t] : null;
            bandFirst[t] = col;
            promptCol[t] = -1;
            answerCol[t] = -1;
            foldedCol[t] = -1;
            if (turn is not null && collapsed.Contains(t))
            {
                foldedCol[t] = col++;
            }
            else
            {
                promptCol[t] = turn?.Prompt is not null ? col++ : -1;
                if (byTurn.TryGetValue(t, out TurnLanes? lanesOfTurn))
                {
                    foreach (int i in MergeByStart(graph, lanesOfTurn)) batchCol[i] = col++;
                }
                answerCol[t] = turn?.Answer is not null ? col++ : -1;
            }
            bandLast[t] = col - 1;
        }
        int columns = col;

        // 2. Rows: the main one first; each subagent in the first free row from its first column.
        var subLanes = new List<Lane>(graph.Lanes.Skip(1));
        var laneRange = new Dictionary<string, int[]>();
        var laneParallel = new Dictionary<string, int>();
        for (int i = 0; i < batches.Count; i++)
        {
            if (hidden[i]) continue;
            Batch b = batches[i];
            if (!laneRange.TryGetValue(b.LaneId, out int[]? r)) laneRange[b.LaneId] = r = [int.MaxValue, -1];
            r[0] = Math.Min(r[0], batchCol[i]);
            r[1] = Math.Max(r[1], batchCol[i]);
            laneParallel[b.LaneId] = Math.Max(laneParallel.GetValueOrDefault(b.LaneId, int.MinValue), b.Steps.Count);
        }
        subLanes.RemoveAll(l => !laneRange.ContainsKey(l.Id));
        subLanes = subLanes.OrderBy(l => laneRange[l.Id][0]).ToList();
        var rowLanes = new List<List<string>> { new() { MainLaneId } };
        var rowEnd = new List<int> { int.MaxValue };
        var rowOfLane = new Dictionary<string, int> { [MainLaneId] = 0 };
        foreach (Lane lane in subLanes)
        {
            int[] range = laneRange[lane.Id];
            int row = -1;
            for (int r = 1; r < rowLanes.Count; r++)
            {
                if (rowEnd[r] < range[0])
                {
                    row = r;
                    break;
                }
            }
            if (row < 0)
            {
                row = rowLanes.Count;
                rowLanes.Add([]);
                rowEnd.Add(-1);
            }
            rowLanes[row].Add(lane.Id);
            rowEnd[row] = range[1];
            rowOfLane[lane.Id] = row;
        }
        var rows = new List<Row>();
        var rowPos = new int[rowLanes.Count];
        int cross = crossStart;
        for (int r = 0; r < rowLanes.Count; r++)
        {
            int parallel = 1;
            foreach (string id in rowLanes[r]) parallel = Math.Max(parallel, laneParallel.GetValueOrDefault(id, 1));
            int size = parallel * crossStep - Gap;
            rowPos[r] = cross;
            rows.Add(new Row(r, cross, size, new ValueList<string>(rowLanes[r])));
            cross += size + RowGap;
        }
        int crossTotal = cross - RowGap + HeaderH / 2;

        // 3. Boxes.
        var boxes = new List<Box>();
        var edges = new List<Edge>();
        var bands = new List<Band>();
        var prompts = new Box?[nTurns];
        var answers = new Box?[nTurns];
        var batchBoxes = new Box[batches.Count][];
        for (int t = 0; t < nTurns; t++)
        {
            if (foldedCol[t] >= 0)
            {
                boxes.Add(MakeBox(v, Kind.Folded, AlongPos(foldedCol[t], alongStep), rowPos[0], cardW, t, MainLaneId, -1, -1, null, null));
            }
            if (promptCol[t] >= 0)
            {
                prompts[t] = MakeBox(v, Kind.Prompt, AlongPos(promptCol[t], alongStep), rowPos[0], cardW, t, MainLaneId, -1, -1, null, null);
                boxes.Add(prompts[t]!);
            }
            if (answerCol[t] >= 0)
            {
                answers[t] = MakeBox(v, Kind.Answer, AlongPos(answerCol[t], alongStep), rowPos[0], cardW, t, MainLaneId, -1, -1, null, null);
                boxes.Add(answers[t]!);
            }
            if (bandLast[t] >= bandFirst[t])
            {
                int start = AlongPos(bandFirst[t], alongStep) - (alongStep - alongExtent) / 2;
                bands.Add(new Band(t, start, (bandLast[t] - bandFirst[t] + 1) * alongStep));
            }
        }
        for (int i = 0; i < batches.Count; i++)
        {
            if (hidden[i]) continue;
            Batch b = batches[i];
            int baseCross = rowPos[rowOfLane[b.LaneId]];
            batchBoxes[i] = new Box[b.Steps.Count];
            for (int s = 0; s < b.Steps.Count; s++)
            {
                Step step = b.Steps[s];
                batchBoxes[i][s] = MakeBox(v, Kind.Step, AlongPos(batchCol[i], alongStep), baseCross + s * crossStep, cardW,
                    Math.Min(b.TurnIndex, nTurns - 1), b.LaneId, i, s, step.CallId, step.State);
                boxes.Add(batchBoxes[i][s]);
            }
        }

        // 4. Edges: the chain of each lane inside each turn, and from each delegating call to its lane.
        for (int t = 0; t < nTurns; t++)
        {
            if (collapsed.Contains(t)) continue;
            if (!byTurn.TryGetValue(t, out TurnLanes? lanes)) lanes = new TurnLanes();
            foreach (string laneId in lanes.Order)
            {
                List<int> ids = lanes.Batches[laneId];
                if (laneId == MainLaneId && prompts[t] is not null && ids.Count > 0)
                {
                    Link(v, edges, [prompts[t]!], batchBoxes[ids[0]], null, null);
                }
                for (int k = 1; k < ids.Count; k++)
                {
                    Batch a = batches[ids[k - 1]];
                    Batch b = batches[ids[k]];
                    Link(v, edges, batchBoxes[ids[k - 1]], batchBoxes[ids[k]], EndOf(a), b.Start);
                }
                if (laneId == MainLaneId && answers[t] is not null && ids.Count > 0)
                {
                    Link(v, edges, batchBoxes[ids[^1]], [answers[t]!], null, null);
                }
            }
            if (prompts[t] is not null && answers[t] is not null && !lanes.Contains(MainLaneId))
            {
                Link(v, edges, [prompts[t]!], [answers[t]!], null, null);
            }
        }
        for (int i = 0; i < batches.Count; i++)
        {
            if (hidden[i]) continue;
            for (int s = 0; s < batches[i].Steps.Count; s++)
            {
                string? sub = batches[i].Steps[s].SubagentId;
                if (sub is null) continue;
                for (int j = 0; j < batches.Count; j++)
                {
                    if (!hidden[j] && batches[j].LaneId == sub && batches[j].Seq == 0)
                    {
                        Box from = batchBoxes[i][s];
                        Box to = batchBoxes[j][0];
                        edges.Add(MakeEdge(v, AlongEnd(v, from), Anchor(v, from), AlongStart(v, to), Anchor(v, to), true, null));
                        break;
                    }
                }
            }
        }

        int alongTotal = columns == 0 ? 2 * Margin : 2 * Margin + (columns - 1) * alongStep + alongExtent;
        return new GraphGeometry(orientation, boxes, edges, bands, rows, v ? crossTotal : alongTotal, v ? alongTotal : crossTotal);
    }

    private static int AlongPos(int col, int alongStep) => Margin + col * alongStep;

    /// <summary>Creates a box from its position on each axis.</summary>
    private static Box MakeBox(bool v, Kind kind, int along, int cross, int cardW, int turn, string lane, int batch, int step,
        string? callId, StepState? state) =>
        v ? new Box(kind, cross, along, cardW, CardH, turn, lane, batch, step, callId, state)
            : new Box(kind, along, cross, cardW, CardH, turn, lane, batch, step, callId, state);

    /// <summary>
    /// Orders the batches of a turn by start time keeping the order of each lane: it always takes the first pending one
    /// with the earliest time, and a batch without a time inherits that of the previous one in its lane.
    /// </summary>
    private static List<int> MergeByStart(FlowGraph graph, TurnLanes lanes)
    {
        var output = new List<int>();
        var next = new Dictionary<string, int>();
        var last = new Dictionary<string, DateTimeOffset>();
        // OrderBy is stable: unknown lanes keep their order of appearance, like Java's List.sort
        List<string> order = lanes.Order.OrderBy(id => LaneIndex(graph, id)).ToList();
        int remaining = lanes.Batches.Values.Sum(l => l.Count);
        while (remaining-- > 0)
        {
            string? best = null;
            DateTimeOffset? bestStart = null;
            foreach (string id in order)
            {
                int pos = next.GetValueOrDefault(id, 0);
                if (pos >= lanes.Batches[id].Count) continue;
                DateTimeOffset? start = graph.Batches[lanes.Batches[id][pos]].Start;
                if (start is null && last.TryGetValue(id, out DateTimeOffset inherited)) start = inherited;
                if (best is null || (start is not null && (bestStart is null || start.Value < bestStart.Value)))
                {
                    best = id;
                    bestStart = start;
                }
            }
            int taken = next.GetValueOrDefault(best!, 0);
            next[best!] = taken + 1;
            output.Add(lanes.Batches[best!][taken]);
            DateTimeOffset? bestBatchStart = graph.Batches[lanes.Batches[best!][taken]].Start;
            if (bestBatchStart is not null) last[best!] = bestBatchStart.Value;
        }
        return output;
    }

    private static int LaneIndex(FlowGraph graph, string id)
    {
        for (int i = 0; i < graph.Lanes.Count; i++)
        {
            if (graph.Lanes[i].Id == id) return i;
        }
        return int.MaxValue;
    }

    private static DateTimeOffset? EndOf(Batch batch)
    {
        DateTimeOffset? end = batch.Start;
        foreach (Step s in batch.Steps)
        {
            if (s.End is not null && (end is null || s.End.Value > end.Value)) end = s.End;
        }
        return end;
    }

    // --- edges: they leave at the end of the box on the long axis and enter at its start ---

    private static int AlongEnd(bool v, Box b) => v ? b.Bottom : b.Right;

    private static int AlongStart(bool v, Box b) => v ? b.Y : b.X;

    /// <summary>Center of the box on the cross axis.</summary>
    private static int Anchor(bool v, Box b) => v ? b.CenterX : b.CenterY;

    private static Edge MakeEdge(bool v, int fromAlong, int fromCross, int toAlong, int toCross, bool dashed, string? label) =>
        v ? new Edge(fromCross, fromAlong, toCross, toAlong, dashed, label)
            : new Edge(fromAlong, fromCross, toAlong, toCross, dashed, label);

    /// <summary>Connects two columns: a fan if one is a single step and the other several, and a single edge if both are batches.</summary>
    private static void Link(bool v, List<Edge> edges, Box[] from, Box[] to, DateTimeOffset? end, DateTimeOffset? start)
    {
        string? label = null;
        if (end is not null && start is not null)
        {
            TimeSpan gap = start.Value - end.Value;
            if (gap >= LongGap) label = "⋯ " + Format(gap);
        }
        int fromAlong = AlongEnd(v, from[0]);
        int toAlong = AlongStart(v, to[0]);
        if (from.Length > 1 && to.Length == 1)
        {
            foreach (Box b in from) edges.Add(MakeEdge(v, fromAlong, Anchor(v, b), toAlong, Anchor(v, to[0]), false, null));
            if (label is not null) edges[^1] = edges[^1] with { Label = label };
        }
        else if (from.Length == 1 && to.Length > 1)
        {
            foreach (Box b in to) edges.Add(MakeEdge(v, fromAlong, Anchor(v, from[0]), toAlong, Anchor(v, b), false, null));
            if (label is not null) edges[^1] = edges[^1] with { Label = label };
        }
        else
        {
            edges.Add(MakeEdge(v, fromAlong, Center(v, from), toAlong, Center(v, to), false, label));
        }
    }

    /// <summary>Center of a column of boxes on the cross axis.</summary>
    private static int Center(bool v, Box[] column)
    {
        Box first = column[0];
        Box last = column[^1];
        return v ? (first.X + last.Right) / 2 : (first.Y + last.Bottom) / 2;
    }

    /// <summary>"N min" or "H h M min". Java's <c>Math.round</c> rounds half up (not to even).</summary>
    internal static string Format(TimeSpan d)
    {
        long seconds = (long)Math.Floor(d.TotalSeconds);
        long minutes = (long)Math.Floor(seconds / 60.0 + 0.5);
        if (minutes >= 60) return (minutes / 60) + " h " + (minutes % 60) + " min";
        return minutes + " min";
    }
}
