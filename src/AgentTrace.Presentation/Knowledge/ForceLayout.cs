using AgentTrace.Core;
using static AgentTrace.Core.Knowledge.KnowledgeGraph;

namespace AgentTrace.Presentation.Knowledge;

/// <summary>
/// Deterministic force-directed layout (Fruchterman–Reingold): no randomness, a node's initial position comes from the hash of
/// its path or, if it has an already placed neighbour, from the neighbour's. When the graph is updated it starts from the previous
/// positions with few iterations, so the drawing does not "jump" when new files arrive. No Avalonia.
/// The hash is Java's <c>String.hashCode()</c> (<see cref="JavaCompat.StringHash"/>), never <c>string.GetHashCode()</c>.
/// </summary>
public static class ForceLayout
{
    public sealed record Point(double X, double Y);

    /// <summary>Ideal length of an edge.</summary>
    internal const double K = 70;

    private const double ColdTemperature = 90;
    private const double WarmTemperature = 4;
    private const double Gravity = 0.03;

    /// <param name="view">What is drawn.</param>
    /// <param name="previous">Positions from last time (may be empty or contain files that are gone).</param>
    /// <param name="iterations">Simulation steps; about 150 cold and 25 warm are enough.</param>
    public static Dictionary<FilePath, Point> Run(KnowledgeView view, IReadOnlyDictionary<FilePath, Point> previous, int iterations)
    {
        IReadOnlyList<Node> nodes = view.Nodes;
        int n = nodes.Count;
        var index = new Dictionary<FilePath, int>();
        for (int i = 0; i < n; i++) index[nodes[i].Path] = i;
        var x = new double[n];
        var y = new double[n];
        var placed = new bool[n];
        int known = 0;
        for (int i = 0; i < n; i++)
        {
            if (previous.TryGetValue(nodes[i].Path, out Point? p))
            {
                x[i] = p.X;
                y[i] = p.Y;
                placed[i] = true;
                known++;
            }
        }
        double temperature = n > 0 && known >= 0.8 * n ? WarmTemperature : ColdTemperature;
        Place(view, index, x, y, placed);

        var dx = new double[n];
        var dy = new double[n];
        for (int it = 0; it < iterations; it++)
        {
            Array.Fill(dx, 0.0);
            Array.Fill(dy, 0.0);
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double ex = x[i] - x[j];
                    double ey = y[i] - y[j];
                    double d = Math.Sqrt(ex * ex + ey * ey);
                    if (d < 0.01)
                    {
                        // coincident: they are separated in a fixed direction per index
                        ex = Math.Cos(i + 7 * j);
                        ey = Math.Sin(i + 7 * j);
                        d = 0.01;
                    }
                    else
                    {
                        ex /= d;
                        ey /= d;
                    }
                    double f = K * K / d;
                    dx[i] += ex * f;
                    dy[i] += ey * f;
                    dx[j] -= ex * f;
                    dy[j] -= ey * f;
                }
            }
            foreach (Edge e in view.Edges)
            {
                if (!index.TryGetValue(e.From, out int a) || !index.TryGetValue(e.To, out int b)) continue;
                double ex = x[a] - x[b];
                double ey = y[a] - y[b];
                double d = Math.Max(Math.Sqrt(ex * ex + ey * ey), 0.01);
                double strength = (e.Relation == Relation.CoChanged ? 1.0 : 0.6) * (1 + 0.5 * Math.Log(Math.Max(e.Weight, 1)));
                double f = d * d / K * strength;
                dx[a] -= ex / d * f;
                dy[a] -= ey / d * f;
                dx[b] += ex / d * f;
                dy[b] += ey / d * f;
            }
            double cooling = 1 - (double)it / Math.Max(iterations, 1);
            double t = temperature * cooling * cooling + 0.4; // quadratic cooling: at the end almost nothing moves
            for (int i = 0; i < n; i++)
            {
                dx[i] -= x[i] * Gravity * K; // soft gravity towards the center: loose components do not drift away
                dy[i] -= y[i] * Gravity * K;
                double len = Math.Sqrt(dx[i] * dx[i] + dy[i] * dy[i]);
                if (len > 0)
                {
                    double step = Math.Min(len, t);
                    x[i] += dx[i] / len * step;
                    y[i] += dy[i] / len * step;
                }
            }
        }
        var output = new Dictionary<FilePath, Point>();
        for (int i = 0; i < n; i++) output[nodes[i].Path] = new Point(x[i], y[i]);
        return output;
    }

    /// <summary>Places the new nodes: next to an already placed neighbour, or at a point derived from the hash of its path.</summary>
    private static void Place(KnowledgeView view, Dictionary<FilePath, int> index, double[] x, double[] y, bool[] placed)
    {
        IReadOnlyList<Node> nodes = view.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (placed[i]) continue;
            int h = Mix(JavaCompat.StringHash(nodes[i].Path.Value));
            double angle = (h & 0xFFFF) / 65536.0 * 2 * Math.PI;
            double cx = 0;
            double cy = 0;
            double radius = 40 + (JavaCompat.UnsignedShiftRight(h, 16) & 0xFF) / 255.0 * 160;
            foreach (Edge e in view.Edges)
            {
                FilePath? other = e.From == nodes[i].Path ? e.To : e.To == nodes[i].Path ? e.From : null;
                if (other is { } o && index.TryGetValue(o, out int oi) && placed[oi])
                {
                    cx = x[oi];
                    cy = y[oi];
                    radius = K * 0.6;
                    break;
                }
            }
            x[i] = cx + Math.Cos(angle) * radius;
            y[i] = cy + Math.Sin(angle) * radius;
            placed[i] = true;
        }
    }

    /// <summary>Java's bit mixing: <c>h ^= h &gt;&gt;&gt; 16; h *= 0x45d9f3b; h ^= h &gt;&gt;&gt; 16</c> (overflowing <c>int</c> arithmetic).</summary>
    private static int Mix(int h)
    {
        h ^= JavaCompat.UnsignedShiftRight(h, 16);
        h = unchecked(h * 0x45d9f3b);
        h ^= JavaCompat.UnsignedShiftRight(h, 16);
        return h;
    }
}
