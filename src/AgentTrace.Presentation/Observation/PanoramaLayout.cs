using System.Globalization;

namespace AgentTrace.Presentation.Observation;

/// <summary>
/// The arrangement of the Panorama tab: which panes are shown and how the two splitters divide the space. <see cref="ColumnRatio"/> is the share
/// of the width taken by the graph column and <see cref="RowRatio"/> the share of the right column's height taken by the Areas map. At least one
/// pane is always shown. The focus (what is selected) is not part of the layout.
/// </summary>
public sealed record PanoramaLayout(bool Graph, bool Areas, bool Knowledge, double ColumnRatio, double RowRatio)
{
    public const double MinRatio = 0.2;
    public const double MaxRatio = 0.8;

    public static readonly PanoramaLayout Default = new(true, true, true, 0.38, 0.40);

    /// <summary>The same layout with the ratios kept within [<see cref="MinRatio"/>, <see cref="MaxRatio"/>] (NaN becomes the default) and, if no pane is shown, all of them.</summary>
    public PanoramaLayout Normalized()
    {
        bool none = !Graph && !Areas && !Knowledge;
        return new PanoramaLayout(Graph || none, Areas || none, Knowledge || none,
            Clamp(ColumnRatio, Default.ColumnRatio), Clamp(RowRatio, Default.RowRatio));
    }

    /// <summary>The same layout showing only the given panes (names <c>graph</c>, <c>areas</c>, <c>knowledge</c>; others are ignored; none left means all).</summary>
    public PanoramaLayout WithPanes(IEnumerable<string> names)
    {
        HashSet<string> set = new(names, StringComparer.OrdinalIgnoreCase);
        return (this with { Graph = set.Contains("graph"), Areas = set.Contains("areas"), Knowledge = set.Contains("knowledge") }).Normalized();
    }

    /// <summary>The text stored in the preference file: <c>key=value</c> lines with the invariant culture.</summary>
    public string Format() => string.Join(Environment.NewLine,
        $"graph={Graph.ToString().ToLowerInvariant()}",
        $"areas={Areas.ToString().ToLowerInvariant()}",
        $"knowledge={Knowledge.ToString().ToLowerInvariant()}",
        $"column={ColumnRatio.ToString("0.####", CultureInfo.InvariantCulture)}",
        $"row={RowRatio.ToString("0.####", CultureInfo.InvariantCulture)}") + Environment.NewLine;

    /// <summary>Reads what <see cref="Format"/> wrote. Lines that are unknown or unreadable are ignored; what is missing keeps its default. Never throws.</summary>
    public static PanoramaLayout Parse(IEnumerable<string> lines)
    {
        PanoramaLayout layout = Default;
        foreach (string line in lines)
        {
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            string key = line[..eq].Trim().ToLowerInvariant();
            string value = line[(eq + 1)..].Trim();
            switch (key)
            {
                case "graph" when bool.TryParse(value, out bool g): layout = layout with { Graph = g }; break;
                case "areas" when bool.TryParse(value, out bool a): layout = layout with { Areas = a }; break;
                case "knowledge" when bool.TryParse(value, out bool k): layout = layout with { Knowledge = k }; break;
                case "column" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double c): layout = layout with { ColumnRatio = c }; break;
                case "row" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double r): layout = layout with { RowRatio = r }; break;
            }
        }
        return layout.Normalized();
    }

    private static double Clamp(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, MinRatio, MaxRatio) : fallback;
}
