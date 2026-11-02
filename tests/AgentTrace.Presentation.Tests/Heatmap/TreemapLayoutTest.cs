using AgentTrace.Core;
using AgentTrace.Presentation.Heatmap;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Presentation.Tests.Heatmap.HeatFixture;

namespace AgentTrace.Presentation.Tests.Heatmap;

public sealed class TreemapLayoutTest
{
    private static HeatTree Sample() => Build(
        Listing(Listed("src/a.cs", 40_000), Listed("src/b.cs", 20_000), Listed("docs/x.md", 30_000), Listed("docs/y.md", 10_000), Listed("z.txt", 5_000)),
        Session(1, Touched("src/a.cs", reads: 4, modified: 2), Touched("docs/y.md", reads: 1)));

    [Fact]
    public void EveryChildIsInsideItsFolderAndAreasFollowTheSize()
    {
        HeatTree tree = Sample();
        TreemapLayout layout = TreemapLayout.Of(tree.Root, 800, 600, HeatArea.Size, ActivityFilter.All);
        TreemapCell root = layout.Cells[0];
        Assert.Same(tree.Root, root.Node);
        Assert.Equal(0, root.Depth);
        Assert.Equal(TreemapLayout.HeaderHeight, root.HeaderHeight);
        Assert.Equal(8, layout.Cells.Count); // the root, 2 folders and 5 files
        TreemapCell Cell(string p) => layout.Cells.Single(c => c.Node.Path == P(p));
        foreach (TreemapCell c in layout.Cells.Skip(1))
        {
            TreemapCell parent = layout.Cells.Single(p => p.Node.Path == c.Node.Path.Parent);
            Assert.True(c.Bounds.X >= parent.Bounds.X + TreemapLayout.Margin - 1e-9 && c.Bounds.Right <= parent.Bounds.Right - TreemapLayout.Margin + 1e-9);
            Assert.True(c.Bounds.Y >= parent.Bounds.Y + parent.HeaderHeight + TreemapLayout.Margin - 1e-9);
            Assert.Equal(parent.Depth + 1, c.Depth);
        }
        double Area(string p) => Cell(p).Bounds.W * Cell(p).Bounds.H;
        Assert.Equal(Area("src/a.cs") / 40_000, Area("src/b.cs") / 20_000, 1e-6);
        Assert.Equal(Area("docs/x.md") / 30_000, Area("docs/y.md") / 10_000, 1e-6);
    }

    [Fact]
    public void ActivityAreaDropsWhatWasNotTouched()
    {
        HeatTree tree = Sample();
        TreemapLayout layout = TreemapLayout.Of(tree.Root, 800, 600, HeatArea.Activity, ActivityFilter.All);
        Assert.Equal(5, layout.Cells.Count);
        Assert.Equal(["docs/y.md", "src/a.cs"], layout.Cells.Where(c => !c.Node.Directory).Select(c => Rel(c.Node)).Order().ToList());
        double Area(string p) => layout.Cells.Single(c => c.Node.Path == P(p)).Bounds is var r ? r.W * r.H : 0;
        // 6 of heat against 1: the areas keep that proportion (the margins of each folder alter it slightly)
        Assert.Equal(6, Area("src/a.cs") / Area("docs/y.md"), 0.5);
    }

    [Fact]
    public void TheFilterChangesWhatCountsAsHeat()
    {
        TreemapLayout layout = TreemapLayout.Of(Sample().Root, 800, 600, HeatArea.Activity, new ActivityFilter(false, true, false));
        Assert.DoesNotContain(layout.Cells, c => c.Node.Path == P("docs/y.md"));
        Assert.Contains(layout.Cells, c => c.Node.Path == P("src/a.cs"));
        Assert.Equal(2, layout.MaxHeat(new ActivityFilter(false, true, false)));
    }

    [Fact]
    public void TheLayoutIsDeterministicAndTiesBreakByPath()
    {
        HeatTree tree = Build(Listing(Listed("b.txt", 5000), Listed("a.txt", 5000), Listed("c.txt", 5000)), Session(0));
        TreemapLayout one = TreemapLayout.Of(tree.Root, 300, 200, HeatArea.Size, ActivityFilter.All);
        TreemapLayout two = TreemapLayout.Of(tree.Root, 300, 200, HeatArea.Size, ActivityFilter.All);
        Assert.Equal(one.Cells.Select(c => (c.Node.Name, c.Bounds)), two.Cells.Select(c => (c.Node.Name, c.Bounds)));
        Assert.Equal(["a.txt", "b.txt", "c.txt"], one.Cells.Skip(1).Select(c => c.Node.Name));
    }

    [Fact]
    public void TinyCellsAreAbsorbedAndSmallFoldersLoseTheirHeader()
    {
        HeatTree tree = Build(Listing(Listed("big.bin", 1_000_000), Listed("tiny.txt", 1)), Session(0));
        TreemapLayout layout = TreemapLayout.Of(tree.Root, 200, 100, HeatArea.Size, ActivityFilter.All);
        Assert.DoesNotContain(layout.Cells, c => c.Node.Name == "tiny.txt"); // 1 KiB against ~1 MB: under 3 px
        Assert.Contains(layout.Cells, c => c.Node.Name == "big.bin");
        Assert.Equal(0, TreemapLayout.Of(tree.Root, 40, 100, HeatArea.Size, ActivityFilter.All).Cells[0].HeaderHeight);
        Assert.Empty(TreemapLayout.Of(tree.Root, 2, 2, HeatArea.Size, ActivityFilter.All).Cells);
    }

    [Fact]
    public void HitTestReturnsTheDeepestCell()
    {
        HeatTree tree = Sample();
        TreemapLayout layout = TreemapLayout.Of(tree.Root, 800, 600, HeatArea.Size, ActivityFilter.All);
        TreemapCell file = layout.Cells.Single(c => c.Node.Path == P("src/a.cs"));
        TreemapCell? hit = layout.HitTest(file.Bounds.X + file.Bounds.W / 2, file.Bounds.Y + file.Bounds.H / 2);
        Assert.Same(file.Node, hit!.Node);
        // The header of the folder belongs to the folder
        TreemapCell src = layout.Cells.Single(c => c.Node.Path == P("src"));
        Assert.Same(src.Node, layout.HitTest(src.Bounds.X + 4, src.Bounds.Y + 4)!.Node);
        Assert.Same(tree.Root, layout.HitTest(0.5, 0.5)!.Node);
        Assert.Null(layout.HitTest(900, 10));
        Assert.Null(layout.HitTest(-1, 10));
    }

    [Fact]
    public void ZoomingLaysOutOnlyThatFolder()
    {
        HeatTree tree = Sample();
        TreemapLayout layout = TreemapLayout.Of(tree.Find(P("docs"))!, 400, 300, HeatArea.Size, ActivityFilter.All);
        Assert.Equal(P("docs"), layout.Cells[0].Node.Path);
        Assert.Equal(3, layout.Cells.Count);
        Assert.Same(tree.Find(P("docs")), layout.Zoom);
    }

    [Fact]
    public void BreadcrumbGoesFromTheProjectDownToTheFolder()
    {
        HeatTree tree = Build(Listing(Listed("a/b/c.txt", 10)), Session(0));
        Assert.Equal([tree.Root.Path.Value, P("a").Value, P("a/b").Value], TreemapLayout.Breadcrumb(tree, P("a/b")).Select(n => n.Path.Value).ToList());
        Assert.Single(TreemapLayout.Breadcrumb(tree, Root));
        Assert.Single(TreemapLayout.Breadcrumb(tree, P("no/existe")));
        Assert.Equal(tree.Root, TreemapLayout.Breadcrumb(tree, P("no/existe"))[0]);
    }

    private static string Rel(HeatNode node) => node.Path.RelativeTo(Root).Replace('\\', '/');
}
