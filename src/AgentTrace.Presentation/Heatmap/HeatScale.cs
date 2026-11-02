using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Heatmap;

/// <summary>Hue a cell is painted with: by file state (as in the tree) or by kind of activity.</summary>
public enum HeatHue { None, Created, Modified, Deleted, Read, Error }

/// <summary>Intensity and hue of the heat. No Avalonia: the UI translates this into colors.</summary>
public static class HeatScale
{
    /// <summary>
    /// 0 if there is no heat; otherwise between 0.25 and 1 on a logarithmic scale (sessions follow a power law,
    /// so a linear scale would leave almost everything at the minimum). <paramref name="max"/>: the highest heat of the current view.
    /// </summary>
    public static double Intensity(double value, double max)
    {
        if (value <= 0) return 0;
        if (max <= 0) return 0.25;
        return Math.Min(1, 0.25 + 0.75 * Math.Log(1 + value) / Math.Log(1 + max));
    }

    /// <summary>
    /// With a single active kind, the hue of that kind. With several, that of the file state (created, modified, deleted), or read or error
    /// if it has no changes; for a folder, that of the heaviest kind (at equal weight: error, write, read).
    /// </summary>
    public static HeatHue Hue(HeatNode node, ActivityFilter filter)
    {
        FileActivity a = node.Activity;
        if (a.Value(filter) == 0) return HeatHue.None;
        if (filter.Count == 1) return filter.Errors ? HeatHue.Error : filter.Writes ? HeatHue.Modified : HeatHue.Read;

        int writes = filter.Writes ? a.Writes : 0;
        int errors = filter.Errors ? a.Errors : 0;
        int reads = filter.Reads ? a.Reads : 0;
        if (!node.Directory)
        {
            if (writes > 0)
            {
                return node.Kind switch
                {
                    ChangeKind.Created => HeatHue.Created,
                    ChangeKind.Deleted => HeatHue.Deleted,
                    _ => HeatHue.Modified,
                };
            }
            return errors > 0 ? HeatHue.Error : HeatHue.Read;
        }
        if (errors >= writes && errors >= reads) return HeatHue.Error;
        return writes >= reads ? HeatHue.Modified : HeatHue.Read;
    }
}
