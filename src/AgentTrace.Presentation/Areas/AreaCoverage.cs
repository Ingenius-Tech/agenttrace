using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Presentation.Tree;

namespace AgentTrace.Presentation.Areas;

/// <summary>How many listed files a dimension classifies. A file counts once, against the rules of the deepest repository that holds it.</summary>
public sealed record DimensionCoverage(AreaDimension Dimension, int Classified, int Total)
{
    /// <summary>Whole percent, rounded down (100 only when every file is classified); 0 with no files.</summary>
    public int Percent => Total == 0 ? 0 : (int)(Classified * 100L / Total);
}

/// <summary>
/// The coverage of the rules over the files the Heatmap lists (disk minus git-ignored minus dot-paths). <c>Truncated</c>: the listing hit
/// its cap, so the numbers describe only the files that were listed.
/// </summary>
public sealed record AreaCoverage(ValueList<DimensionCoverage> Dimensions, bool Truncated)
{
    public static readonly AreaCoverage Empty = new(ValueList<DimensionCoverage>.Empty, false);

    public static AreaCoverage Of(AreaRepoSet repos, ProjectListing listing)
    {
        int layer = 0, module = 0, kind = 0, total = 0;
        foreach (ListedFile file in listing.Files)
        {
            if (file.Ghost) continue;
            (AreaRepo? repo, AreaAssignment assignment) = repos.Classify(file.Path);
            if (repo is null) continue;
            total++;
            if (assignment.Layer is not null) layer++;
            if (assignment.Module is not null) module++;
            if (assignment.Kind is not null) kind++;
        }
        return new AreaCoverage(
            new[]
            {
                new DimensionCoverage(AreaDimension.Layer, layer, total),
                new DimensionCoverage(AreaDimension.Module, module, total),
                new DimensionCoverage(AreaDimension.Kind, kind, total),
            }.ToValueList(),
            listing.Truncated);
    }
}
