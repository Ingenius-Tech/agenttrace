using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Presentation.Areas;
using AgentTrace.Presentation.Tree;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Areas;

public sealed class AreaCoverageTest : TempDirTestBase
{
    private static AreaRule Rule(string id, AreaDimension dimension, params string[] include) =>
        new(id, id, dimension, include.ToValueList(), ValueList<string>.Empty);

    private AreaRepoSet Repo(params AreaRule[] rules)
    {
        var set = new AreaRules(rules.ToValueList());
        return new AreaRepoSet([new AreaRepo(Dir, "repo", set, new AreaClassifier(Dir, set), AreaSource.File, ValueList<AreaRuleError>.Empty)]);
    }

    private ProjectListing Listing(bool truncated, params string[] files) =>
        new(files.Select(f => new ListedFile(Dir.Combine(f.Split('/')), 1, false)).ToValueList(), truncated);

    [Fact]
    public void EachDimensionCountsTheListedFilesItClassifies()
    {
        AreaRepoSet repos = Repo(Rule("api", AreaDimension.Layer, "src/Api/**"), Rule("all", AreaDimension.Module, "**"), Rule("docs", AreaDimension.Kind, "*.md"));
        AreaCoverage coverage = AreaCoverage.Of(repos, Listing(false, "src/Api/a.cs", "src/Data/b.cs", "README.md", "docs/x.md"));

        Assert.Equal([new DimensionCoverage(AreaDimension.Layer, 1, 4), new DimensionCoverage(AreaDimension.Module, 4, 4), new DimensionCoverage(AreaDimension.Kind, 2, 4)], coverage.Dimensions.ToArray());
        Assert.Equal([25, 100, 50], coverage.Dimensions.Select(d => d.Percent).ToArray());
        Assert.False(coverage.Truncated);
    }

    [Fact]
    public void ThePercentageRoundsDownSoPartialCoverageNeverReadsAsComplete()
    {
        AreaRepoSet repos = Repo(Rule("a", AreaDimension.Layer, "a/**"));
        AreaCoverage coverage = AreaCoverage.Of(repos, Listing(false, "a/1", "a/2", "b/3"));
        Assert.Equal(66, coverage.Dimensions[0].Percent);
        Assert.Equal(99, new DimensionCoverage(AreaDimension.Layer, 999, 1000).Percent);
    }

    [Fact]
    public void ATruncatedListingIsFlaggedAndGhostsAreNotCounted()
    {
        AreaRepoSet repos = Repo(Rule("a", AreaDimension.Layer, "**"));
        var listing = new ProjectListing(new[] { new ListedFile(Dir.Combine("x"), 1, false), new ListedFile(Dir.Combine("gone"), 0, true) }.ToValueList(), true);
        AreaCoverage coverage = AreaCoverage.Of(repos, listing);
        Assert.True(coverage.Truncated);
        Assert.Equal(1, coverage.Dimensions[0].Total);
    }

    [Fact]
    public void WithoutFilesThePercentageIsZeroNotAnError()
    {
        Assert.Equal(0, AreaCoverage.Of(Repo(Rule("a", AreaDimension.Layer, "**")), Listing(false)).Dimensions[0].Percent);
    }
}
