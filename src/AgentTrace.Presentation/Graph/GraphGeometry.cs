using AgentTrace.Core;
using static AgentTrace.Core.Flow.FlowGraph;

namespace AgentTrace.Presentation.Graph;

/// <summary>
/// Position of each graph element in world coordinates (no zoom or scroll). No Avalonia: the graph control draws it and
/// selection and navigation query it. Boxes are ordered by the long axis (time) and then by the cross one, whatever the
/// <see cref="GraphOrientation"/>.
/// </summary>
public sealed class GraphGeometry
{
    public const int CardW = 176;
    public const int CardH = 46;
    public const int ColW = 200;
    public const int Gap = 8;

    /// <summary>When vertical the cards are wider (more text fits) and batches follow each other every <c>RowStep</c> pixels.</summary>
    public const int VCardW = 200;

    public const int RowStep = 62;

    /// <summary><c>Folded</c>: the summary card of a folded turn.</summary>
    public enum Kind { Prompt, Step, Answer, Folded }

    /// <summary>A box. <c>CallId</c> and <c>State</c> only on steps; <c>BatchIndex</c> is the index into <c>FlowGraph.Batches</c>.</summary>
    public sealed record Box(Kind Kind, int X, int Y, int W, int H, int TurnIndex, string LaneId, int BatchIndex, int StepIndex,
        string? CallId, StepState? State)
    {
        public int Right => X + W;

        public int Bottom => Y + H;

        public int CenterX => X + W / 2;

        public int CenterY => Y + H / 2;

        public bool Contains(double px, double py) => px >= X && px < X + W && py >= Y && py < Y + H;
    }

    /// <summary>Edge from <c>(X1,Y1)</c> to <c>(X2,Y2)</c>; <c>Label</c> marks a long gap between batches.</summary>
    public sealed record Edge(int X1, int Y1, int X2, int Y2, bool Dashed, string? Label);

    /// <summary>Band of a turn: it spans <c>[Start, Start + Length)</c> on the long axis and crosses all lanes.</summary>
    public sealed record Band(int TurnIndex, int Start, int Length);

    /// <summary>Row (or column, when vertical) of lanes: <c>[Start, Start + Size)</c> on the cross axis. Several subagent lanes share a row if they do not overlap on the long axis.</summary>
    public sealed record Row(int Index, int Start, int Size, ValueList<string> LaneIds);

    public sealed record Rect(double X, double Y, double W, double H)
    {
        public double Right => X + W;

        public double Bottom => Y + H;
    }

    private readonly Dictionary<string, Box> _stepsById = [];

    internal GraphGeometry(GraphOrientation orientation, IEnumerable<Box> boxes, IEnumerable<Edge> edges, IEnumerable<Band> bands,
        IEnumerable<Row> rows, int width, int height)
    {
        Orientation = orientation;
        // OrderBy is stable, like Java's List.sort
        Boxes = (orientation.IsVertical() ? boxes.OrderBy(b => b.Y).ThenBy(b => b.X) : boxes.OrderBy(b => b.X).ThenBy(b => b.Y)).ToList();
        Edges = edges.ToList();
        Bands = bands.ToList();
        Rows = rows.ToList();
        Width = width;
        Height = height;
        foreach (Box b in Boxes)
        {
            if (b.CallId is not null) _stepsById[b.CallId] = b;
        }
    }

    public GraphOrientation Orientation { get; }

    public IReadOnlyList<Box> Boxes { get; }

    public IReadOnlyList<Edge> Edges { get; }

    public IReadOnlyList<Band> Bands { get; }

    public IReadOnlyList<Row> Rows { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The turn band covering that position of the long axis (to fold or unfold with a click on its header).</summary>
    public Band? BandAt(double along)
    {
        foreach (Band b in Bands)
        {
            if (along >= b.Start && along < b.Start + b.Length) return b;
        }
        return null;
    }

    /// <summary>Edge of the last column on the long axis: what counts as "the end" when following the session.</summary>
    public int ContentEnd
    {
        get
        {
            if (Boxes.Count == 0) return 0;
            Box last = Boxes[^1];
            return Orientation.IsVertical() ? last.Bottom : last.Right;
        }
    }

    /// <summary>The box of that step, or null (<c>step(callId)</c> in Java).</summary>
    public Box? FindStep(string callId) => _stepsById.GetValueOrDefault(callId);

    /// <summary>The box of the prompt or the answer of a turn, or null.</summary>
    public Box? FindTurnBox(int turnIndex, Kind kind) => Boxes.FirstOrDefault(b => b.Kind == kind && b.TurnIndex == turnIndex);

    /// <summary>The box under a world point, or null. Binary search by column: it does not walk the whole graph.</summary>
    public Box? Hit(double x, double y)
    {
        double along = Orientation.IsVertical() ? y : x;
        for (int i = LastStartingAtOrBefore(along); i >= 0; i--)
        {
            Box b = Boxes[i];
            if (Start(b) + AlongExtent() <= along) break;
            if (b.Contains(x, y)) return b;
        }
        return null;
    }

    /// <summary>The boxes touching the rectangle, in the order of <see cref="Boxes"/>.</summary>
    public IReadOnlyList<Box> Visible(Rect r)
    {
        bool v = Orientation.IsVertical();
        double from = v ? r.Y : r.X;
        double to = v ? r.Bottom : r.Right;
        var output = new List<Box>();
        for (int i = FirstEndingAfter(from); i < Boxes.Count; i++)
        {
            Box b = Boxes[i];
            if (Start(b) > to) break;
            if (b.Right > r.X && b.X < r.Right && b.Bottom > r.Y && b.Y < r.Bottom) output.Add(b);
        }
        return output;
    }

    private int Start(Box b) => Orientation.IsVertical() ? b.Y : b.X;

    /// <summary>What every box measures on the long axis.</summary>
    private int AlongExtent() => Orientation.IsVertical() ? CardH : CardW;

    private int LastStartingAtOrBefore(double along)
    {
        int lo = 0;
        int hi = Boxes.Count - 1;
        int found = -1;
        while (lo <= hi)
        {
            int mid = (int)((uint)(lo + hi) >> 1);
            if (Start(Boxes[mid]) <= along)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return found;
    }

    private int FirstEndingAfter(double along)
    {
        int lo = 0;
        int hi = Boxes.Count;
        while (lo < hi)
        {
            int mid = (int)((uint)(lo + hi) >> 1);
            if (Start(Boxes[mid]) + AlongExtent() > along) hi = mid;
            else lo = mid + 1;
        }
        return lo;
    }
}
