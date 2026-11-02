using Rect = AgentTrace.Presentation.Graph.GraphGeometry.Rect;

namespace AgentTrace.Presentation.Heatmap;

/// <summary>
/// Squarified treemap (Bruls, Huizing and van Wijk, 2000): splits a rectangle into as many as there are weights, with area proportional
/// to the weight and as close to a square as possible. Deterministic: the same order of weights gives the same layout.
/// </summary>
public static class Squarify
{
    /// <summary>
    /// One rectangle per weight, in the same order. Weights must come from largest to smallest (this gives the best proportion);
    /// a weight of 0 or negative gets an empty rectangle.
    /// </summary>
    public static IReadOnlyList<Rect> Layout(IReadOnlyList<double> weights, Rect bounds)
    {
        var result = new Rect[weights.Count];
        double total = weights.Where(w => w > 0).Sum();
        Array.Fill(result, new Rect(bounds.X, bounds.Y, 0, 0));
        if (total <= 0 || bounds.W <= 0 || bounds.H <= 0) return result;

        double scale = bounds.W * bounds.H / total;
        double x = bounds.X, y = bounds.Y, w = bounds.W, h = bounds.H;
        int i = 0;
        while (i < weights.Count && weights[i] > 0)
        {
            double side = Math.Min(w, h);
            // Items are added to the row as long as its worst aspect ratio does not get worse
            double sum = 0;
            double worst = double.PositiveInfinity;
            int end = i;
            while (end < weights.Count && weights[end] > 0)
            {
                double area = weights[end] * scale;
                double candidate = Worst(sum + area, weights[i] * scale, area, side);
                if (end > i && candidate > worst) break;
                sum += area;
                worst = candidate;
                end++;
            }
            // The row takes a strip of thickness sum/side along the short side
            double thickness = sum / side;
            double offset = 0;
            for (int k = i; k < end; k++)
            {
                double length = weights[k] * scale / thickness;
                result[k] = w >= h
                    ? new Rect(x, y + offset, thickness, length)
                    : new Rect(x + offset, y, length, thickness);
                offset += length;
            }
            if (w >= h)
            {
                x += thickness;
                w -= thickness;
            }
            else
            {
                y += thickness;
                h -= thickness;
            }
            i = end;
        }
        return result;
    }

    /// <summary>The worst aspect ratio of a row of total area <paramref name="sum"/>, given its largest and smallest item.</summary>
    private static double Worst(double sum, double max, double min, double side) =>
        Math.Max(side * side * max / (sum * sum), sum * sum / (side * side * min));
}
