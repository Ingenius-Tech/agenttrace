using System.Diagnostics;
using AgentTrace.Core.Flow;
using static AgentTrace.Core.Flow.FlowGraph;
using static AgentTrace.Presentation.Graph.GraphGeometry;

namespace AgentTrace.Presentation.Graph;

/// <summary>Keyboard navigation between the graph boxes. Pure logic: it returns the box to move to.</summary>
public static class GraphNavigator
{
    public enum Move { Left, Right, Up, Down, First, Last, NextError }

    /// <summary>With no current selection, any move goes to the first box (or the last with <c>Last</c>). Null if there is nowhere to go.</summary>
    public static Box? MoveFrom(GraphGeometry geometry, Box? current, Move move)
    {
        IReadOnlyList<Box> boxes = geometry.Boxes;
        if (boxes.Count == 0) return null;
        return move switch
        {
            Move.First => boxes[0],
            Move.Last => boxes[^1],
            Move.NextError => NextError(boxes, current),
            Move.Left or Move.Right => current is null ? boxes[0] : Horizontal(boxes, current, move == Move.Right),
            Move.Up or Move.Down => current is null ? boxes[0] : Vertical(boxes, current, move == Move.Down),
            _ => throw new UnreachableException(),
        };
    }

    /// <summary>The box of the previous or next column: from the same lane if any, otherwise the closest in height.</summary>
    private static Box? Horizontal(IReadOnlyList<Box> boxes, Box cur, bool forward)
    {
        IEnumerable<Box> candidates = boxes.Where(b => forward ? b.X > cur.X : b.X < cur.X);
        return Nearest(candidates.Where(b => b.LaneId == cur.LaneId), cur) ?? Nearest(candidates, cur);
    }

    /// <summary>The box above or below: first the parallel ones of the same column and then those of other lanes.</summary>
    private static Box? Vertical(IReadOnlyList<Box> boxes, Box cur, bool down) =>
        Nearest(boxes.Where(b => down ? b.Y > cur.Y : b.Y < cur.Y), cur);

    /// <summary>The closest in X and, at equal distance, in Y; the first in box order on a tie (<c>Stream.min</c>).</summary>
    private static Box? Nearest(IEnumerable<Box> candidates, Box cur)
    {
        Box? best = null;
        int bestDx = 0;
        int bestDy = 0;
        foreach (Box b in candidates)
        {
            int dx = Math.Abs(b.X - cur.X);
            int dy = Math.Abs(b.Y - cur.Y);
            if (best is null || dx < bestDx || (dx == bestDx && dy < bestDy))
            {
                best = b;
                bestDx = dx;
                bestDy = dy;
            }
        }
        return best;
    }

    /// <summary>The next error in reading order, wrapping around at the end.</summary>
    private static Box? NextError(IReadOnlyList<Box> boxes, Box? cur)
    {
        int start = -1;
        if (cur is not null)
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Equals(cur))
                {
                    start = i;
                    break;
                }
            }
        }
        for (int i = 1; i <= boxes.Count; i++)
        {
            Box b = boxes[(start + i) % boxes.Count];
            if (b.State == StepState.Error) return b;
        }
        return null;
    }
}
