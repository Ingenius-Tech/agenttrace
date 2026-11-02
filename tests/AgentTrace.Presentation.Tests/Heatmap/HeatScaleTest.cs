using AgentTrace.Presentation.Heatmap;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Presentation.Tests.Heatmap.HeatFixture;

namespace AgentTrace.Presentation.Tests.Heatmap;

public sealed class HeatScaleTest
{
    private static readonly ActivityFilter OnlyReads = new(true, false, false);
    private static readonly ActivityFilter OnlyWrites = new(false, true, false);
    private static readonly ActivityFilter OnlyErrors = new(false, false, true);

    [Fact]
    public void IntensityIsZeroWithoutHeatAndLogarithmicOtherwise()
    {
        Assert.Equal(0, HeatScale.Intensity(0, 10));
        Assert.Equal(1, HeatScale.Intensity(10, 10), 1e-12);
        Assert.Equal(0.25 + 0.75 * Math.Log(2) / Math.Log(11), HeatScale.Intensity(1, 10), 1e-12);
        Assert.True(HeatScale.Intensity(1, 1000) > 0.25);
        // Logarithmic: half of the maximum is already well above half of the scale
        Assert.True(HeatScale.Intensity(500, 1000) > 0.9);
    }

    [Fact]
    public void IntensityGrowsAndStaysInRange()
    {
        double previous = 0;
        for (int v = 1; v <= 100; v++)
        {
            double t = HeatScale.Intensity(v, 100);
            Assert.True(t > previous && t is >= 0.25 and <= 1);
            previous = t;
        }
        Assert.Equal(1, HeatScale.Intensity(200, 100)); // above the maximum it does not exceed 1
        Assert.Equal(0.25, HeatScale.Intensity(3, 0));
    }

    [Fact]
    public void WithASingleActiveTypeTheHueIsTheTypeOne()
    {
        HeatTree tree = TreeOf(Touched("a.cs", reads: 1, created: 1, errors: 1, last: ChangeKind.Created));
        HeatNode a = tree.Find(P("a.cs"))!;
        Assert.Equal(HeatHue.Read, HeatScale.Hue(a, OnlyReads));
        Assert.Equal(HeatHue.Modified, HeatScale.Hue(a, OnlyWrites));
        Assert.Equal(HeatHue.Error, HeatScale.Hue(a, OnlyErrors));
    }

    [Fact]
    public void WithSeveralTypesAFileTakesItsChangeKind()
    {
        HeatTree tree = TreeOf(
            Touched("c.cs", created: 1, last: ChangeKind.Created),
            Touched("m.cs", reads: 5, modified: 1, last: ChangeKind.Modified),
            Touched("d.cs", deleted: 1, last: ChangeKind.Deleted),
            Touched("r.cs", reads: 2),
            Touched("e.cs", errors: 1),
            Touched("x.cs", external: 1, last: ChangeKind.Modified),
            Touched("n.cs"));
        HeatHue Hue(string file, ActivityFilter filter) => HeatScale.Hue(tree.Find(P(file))!, filter);
        Assert.Equal(HeatHue.Created, Hue("c.cs", ActivityFilter.All));
        Assert.Equal(HeatHue.Modified, Hue("m.cs", ActivityFilter.All));
        Assert.Equal(HeatHue.Deleted, Hue("d.cs", ActivityFilter.All));
        Assert.Equal(HeatHue.Read, Hue("r.cs", ActivityFilter.All));
        Assert.Equal(HeatHue.Error, Hue("e.cs", ActivityFilter.All));
        Assert.Equal(HeatHue.Modified, Hue("x.cs", ActivityFilter.All));
        Assert.Equal(HeatHue.None, Hue("n.cs", ActivityFilter.All));
        // A write that the filter excludes does not tint: the read remains
        Assert.Equal(HeatHue.Read, Hue("m.cs", new ActivityFilter(true, false, true)));
        Assert.Equal(HeatHue.None, Hue("d.cs", new ActivityFilter(true, false, true)));
    }

    [Fact]
    public void AFolderTakesTheTypeThatWeighsMore()
    {
        HeatTree tree = Build(
            Listing(Listed("w/a.cs", 10), Listed("w/b.cs", 10), Listed("r/a.cs", 10), Listed("e/a.cs", 10), Listed("t/a.cs", 10)),
            Session(1, Touched("w/a.cs", modified: 3, last: ChangeKind.Modified), Touched("w/b.cs", reads: 1),
                Touched("r/a.cs", reads: 4), Touched("e/a.cs", reads: 2, errors: 2)));
        Assert.Equal(HeatHue.Modified, HeatScale.Hue(tree.Find(P("w"))!, ActivityFilter.All));
        Assert.Equal(HeatHue.Read, HeatScale.Hue(tree.Find(P("r"))!, ActivityFilter.All));
        Assert.Equal(HeatHue.Error, HeatScale.Hue(tree.Find(P("e"))!, ActivityFilter.All)); // tie: error
        Assert.Equal(HeatHue.None, HeatScale.Hue(tree.Find(P("t"))!, ActivityFilter.All));
    }
}
