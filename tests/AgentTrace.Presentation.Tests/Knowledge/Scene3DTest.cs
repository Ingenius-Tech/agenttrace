using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Presentation.Knowledge;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Presentation.Tests.Knowledge;

public sealed class Scene3DTest
{
    private const double W = 800;
    private const double H = 600;

    private static readonly FilePath Root = new(OperatingSystem.IsWindows() ? @"C:\project" : "/project");

    private static EventHeader Hd() => new(new SessionId("s"), "test", null, null, new SourceRef(new FilePath("t"), 1));

    private static FilePath P(string name) => Root.Combine(name);

    /// <summary>A new file in each of the three turns.</summary>
    private static KnowledgeGraph Layers()
    {
        var b = new KnowledgeBuilder();
        foreach (string n in new[] { "a", "b", "c" })
        {
            b.Accept(new UserMessage(Hd(), "p"));
            b.Accept(new FileChanged(Hd(), P(n), ChangeKind.Modified, "w"));
        }
        return b.Snapshot();
    }

    private static Dictionary<FilePath, ForceLayout.Point> Spread() => new()
    {
        [P("a")] = new(-100, 0), [P("b")] = new(0, 0), [P("c")] = new(100, 50),
    };

    [Fact]
    public void ZIsMonotonicInTheFirstTurnAndXyIsThe2DLayout()
    {
        KnowledgeGraph g = Layers();
        KnowledgeView view = KnowledgeView.Of(g, false, KnowledgeView.MaxNodes);
        Dictionary<FilePath, ForceLayout.Point> flat = Spread();
        Dictionary<FilePath, Point3> placed = Scene3D.Place(view, flat, g.Turns);
        Assert.True(placed[P("a")].Z < placed[P("b")].Z && placed[P("b")].Z < placed[P("c")].Z);
        Assert.Equal(0, placed[P("a")].Z);
        foreach (FilePath f in flat.Keys)
        {
            Assert.Equal(flat[f].X, placed[f].X);
            Assert.Equal(flat[f].Y, placed[f].Y);
        }
    }

    [Fact]
    public void AFileTouchedAgainLaterGetsATrailFromItsFirstToItsLastTurnAndTheRestDoNot()
    {
        var b = new KnowledgeBuilder();
        b.Accept(new UserMessage(Hd(), "p"));
        b.Accept(new FileChanged(Hd(), P("a"), ChangeKind.Modified, "w"));
        b.Accept(new FileRead(Hd(), P("b"), "r"));
        b.Accept(new UserMessage(Hd(), "p"));
        b.Accept(new FileChanged(Hd(), P("c"), ChangeKind.Modified, "w"));
        b.Accept(new UserMessage(Hd(), "p"));
        b.Accept(new FileRead(Hd(), P("a"), "r")); // a is touched again in turn 3
        KnowledgeGraph g = b.Snapshot();
        KnowledgeView view = KnowledgeView.Of(g, false, KnowledgeView.MaxNodes);
        var flat = new Dictionary<FilePath, ForceLayout.Point> { [P("a")] = new(-50, 0), [P("b")] = new(0, 0), [P("c")] = new(50, 20) };
        Dictionary<FilePath, Point3> placed = Scene3D.Place(view, flat, g.Turns);
        double gap = Scene3D.LayerGap(flat, g.Turns);

        List<Scene3D.Trail> trails = Scene3D.Trails(view, placed, gap);

        Scene3D.Trail only = Assert.Single(trails);
        Assert.Equal(P("a"), only.Node.Path);
        Assert.Equal(0, only.From.Z);          // still at its first turn
        Assert.Equal(2 * gap, only.To.Z, 9);   // reaches the third (index 2)
        Assert.Equal(only.From.X, only.To.X);
        Assert.Equal(only.From.Y, only.To.Y);
    }

    [Fact]
    public void TheTargetProjectsToTheCenterAndWhatIsFartherLooksSmaller()
    {
        var cam = new Camera3D();
        cam.SetTarget(10, 20, 30);
        cam.FreeView();
        Projected t = cam.Project(10, 20, 30, W, H);
        Assert.Equal(W / 2, t.X, 6);
        Assert.Equal(H / 2, t.Y, 6);
        // A point farther from the camera (towards the inside of the scene) has fewer pixels per unit
        Projected far = cam.Project(10 + 300 * Math.Cos(cam.Yaw + Math.PI), 20 - 300 * Math.Sin(cam.Yaw + Math.PI), 30, W, H);
        Projected near = cam.Project(10 + 300 * Math.Cos(cam.Yaw), 20 - 300 * Math.Sin(cam.Yaw), 30, W, H);
        Assert.True(far.Scale < t.Scale && t.Scale < near.Scale);
    }

    [Fact]
    public void ATurnOf360DegreesComesBack()
    {
        var cam = new Camera3D();
        cam.FreeView();
        Projected before = cam.Project(40, -25, 70, W, H);
        cam.Orbit(Math.PI * 2, 0);
        Projected after = cam.Project(40, -25, 70, W, H);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
    }

    [Fact]
    public void TheTopViewKeepsTheOrientationOfThe2D()
    {
        var cam = new Camera3D();
        cam.TopView();
        Projected origin = cam.Project(0, 0, 0, W, H);
        Projected right = cam.Project(50, 0, 0, W, H);
        Projected down = cam.Project(0, 50, 0, W, H);
        Assert.True(right.X > origin.X && Math.Abs(right.Y - origin.Y) < 1e-6);
        Assert.True(down.Y > origin.Y && Math.Abs(down.X - origin.X) < 1e-6);
    }

    [Fact]
    public void TheSideViewPutsTimeUpwards()
    {
        var cam = new Camera3D();
        cam.SideView();
        Assert.True(cam.Project(0, 0, 80, W, H).Y < cam.Project(0, 0, 0, W, H).Y);
    }

    [Fact]
    public void ZoomChangesTheDistanceWithinLimits()
    {
        var cam = new Camera3D();
        double d = cam.Distance;
        cam.Zoom(2);
        Assert.Equal(d / 2, cam.Distance, 6);
        for (int i = 0; i < 100; i++) cam.Zoom(2);
        Assert.Equal(Camera3D.MinDistance, cam.Distance);
    }

    [Fact]
    public void PanDragsTheContentWithThePointer()
    {
        var cam = new Camera3D();
        cam.FreeView();
        Projected before = cam.Project(0, 0, 0, W, H);
        cam.Pan(40, -25, H);
        Projected after = cam.Project(0, 0, 0, W, H);
        Assert.Equal(before.X + 40, after.X, 6);
        Assert.Equal(before.Y - 25, after.Y, 6);
    }

    [Fact]
    public void ProjectionIsFiniteAndSortedFarToNearAndTheNearestWinsTheClick()
    {
        KnowledgeGraph g = Layers();
        KnowledgeView view = KnowledgeView.Of(g, false, KnowledgeView.MaxNodes);
        // The three on the same vertical seen from the side... but head-on they overlap: the camera looks along the X axis
        var placed = new Dictionary<FilePath, Point3>
        {
            [P("a")] = new(0, 0, 0), [P("b")] = new(0, 0, 0), [P("c")] = new(0, 0, 0),
        };
        var cam = new Camera3D();
        cam.SetView(0, 0); // eye on +X: what has greater X is nearer
        placed[P("c")] = new Point3(60, 0, 0);
        placed[P("a")] = new Point3(-60, 0, 0);
        List<Scene3D.Item> items = Scene3D.Project(view, placed, cam, W, H);
        Assert.Equal(3, items.Count);
        Assert.All(items, i => Assert.True(double.IsFinite(i.X) && double.IsFinite(i.Y) && double.IsFinite(i.Radius)));
        Assert.True(items[0].Depth >= items[1].Depth && items[1].Depth >= items[2].Depth);
        Assert.Equal(P("a"), items[0].Node.Path);
        Assert.Equal(P("c"), items[^1].Node.Path);
        Assert.Equal(P("c"), Scene3D.HitTest(items, W / 2, H / 2)!.Path);
        Assert.Null(Scene3D.HitTest(items, 5, 5));
    }

    [Fact]
    public void FitMakesTheWholeBoxVisible()
    {
        var cam = new Camera3D();
        cam.FreeView();
        var corners = new[] { new Point3(-200, -100, 0), new Point3(200, 100, 120) };
        (Point3 min, Point3 max) = Scene3D.Bounds(corners);
        cam.Fit(min, max, W, H);
        foreach (double x in new[] { min.X, max.X })
        foreach (double y in new[] { min.Y, max.Y })
        foreach (double z in new[] { min.Z, max.Z })
        {
            Projected p = cam.Project(x, y, z, W, H);
            Assert.True(p.Visible && p.X > 0 && p.X < W && p.Y > 0 && p.Y < H, $"({x},{y},{z}) → ({p.X:0},{p.Y:0})");
        }
    }
}
