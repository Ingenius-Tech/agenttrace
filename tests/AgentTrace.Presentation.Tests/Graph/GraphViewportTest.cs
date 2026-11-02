using AgentTrace.Presentation.Graph;
using static AgentTrace.Presentation.Graph.GraphGeometry;

namespace AgentTrace.Presentation.Tests.Graph;

public sealed class GraphViewportTest
{
    private const double Tolerance = 1e-9;

    private static GraphViewport NewViewport()
    {
        var v = new GraphViewport();
        v.SetViewSize(1000, 500);
        v.SetContentSize(5000, 400);
        return v;
    }

    [Fact]
    public void FollowsTheEndByDefaultAndWhileContentGrows()
    {
        GraphViewport v = NewViewport();
        Assert.True(v.Following);
        Assert.Equal(4000, v.ScrollX, Tolerance);
        v.SetContentSize(6000, 400);
        Assert.Equal(5000, v.ScrollX, Tolerance);
    }

    [Fact]
    public void ScrollingBackStopsFollowingAndGoToEndResumes()
    {
        GraphViewport v = NewViewport();
        v.ScrollBy(-300, 0);
        Assert.False(v.Following);
        v.SetContentSize(6000, 400);
        Assert.Equal(3700, v.ScrollX, Tolerance);
        v.GoToEnd();
        Assert.True(v.Following);
        Assert.Equal(5000, v.ScrollX, Tolerance);
    }

    [Fact]
    public void ScrollingForwardToTheEndFollowsAgain()
    {
        GraphViewport v = NewViewport();
        v.ScrollBy(-300, 0);
        v.ScrollBy(300, 0);
        Assert.True(v.Following);
    }

    [Fact]
    public void ScrollIsClampedToTheContent()
    {
        GraphViewport v = NewViewport();
        v.ScrollBy(-99999, -99999);
        Assert.Equal(0, v.ScrollX);
        Assert.Equal(0, v.ScrollY);
        v.ScrollBy(99999, 99999);
        Assert.Equal(4000, v.ScrollX, Tolerance);
        Assert.Equal(0, v.ScrollY);
    }

    [Fact]
    public void ZoomIsLimitedAndKeepsTheAnchorFixed()
    {
        GraphViewport v = NewViewport();
        v.ScrollBy(-2000, 0);
        double before = v.ToWorldX(400);
        v.ZoomBy(1.5, 400, 100);
        Assert.Equal(1.5, v.Zoom, Tolerance);
        Assert.Equal(before, v.ToWorldX(400), 1e-6);
        v.ZoomBy(100, 0, 0);
        Assert.Equal(GraphViewport.MaxZoom, v.Zoom);
        v.ZoomBy(1e-6, 0, 0);
        Assert.Equal(GraphViewport.MinZoom, v.Zoom);
    }

    [Fact]
    public void DetailDependsOnTheZoom()
    {
        GraphViewport v = NewViewport();
        Assert.True(v.Detailed);
        v.SetZoom(0.5, 0, 0);
        Assert.False(v.Detailed);
        v.SetZoom(GraphViewport.DetailZoom, 0, 0);
        Assert.True(v.Detailed);
    }

    [Fact]
    public void VisibleRectShrinksWhenZoomingIn()
    {
        GraphViewport v = NewViewport();
        Assert.Equal(1000, v.VisibleRect().W, Tolerance);
        v.SetZoom(2, 0, 0);
        Assert.Equal(500, v.VisibleRect().W, Tolerance);
        Assert.Equal(250, v.VisibleRect().H, Tolerance);
    }

    [Fact]
    public void RevealingAnOldBoxStopsFollowingAndTheLastOneKeepsIt()
    {
        GraphViewport v = NewViewport();
        var old = new Box(Kind.Step, 100, 40, 176, 46, 0, "main", 0, 0, "a", null);
        v.Reveal(old, 5000);
        Assert.False(v.Following);
        Assert.True(v.VisibleRect().X <= 100 && 276 <= v.VisibleRect().Right);
        var last = new Box(Kind.Step, 4824, 40, 176, 46, 0, "main", 9, 0, "z", null);
        v.Reveal(last, 5000);
        Assert.True(v.Following);
    }

    [Fact]
    public void InVerticalTheEndIsTheBottom()
    {
        var v = new GraphViewport();
        v.SetViewSize(1000, 500);
        v.SetVertical(true);
        v.SetContentSize(1200, 5000);
        Assert.True(v.Following);
        Assert.Equal(4500, v.ScrollY, Tolerance);
        Assert.Equal(0, v.ScrollX, Tolerance);
        v.SetContentSize(1200, 6000);
        Assert.Equal(5500, v.ScrollY, Tolerance);
        v.ScrollBy(0, -300);
        Assert.False(v.Following);
        v.ScrollBy(300, 0);
        Assert.False(v.Following);
        v.GoToEnd();
        Assert.True(v.Following);
        Assert.Equal(5500, v.ScrollY, Tolerance);
    }

    [Fact]
    public void RevealingInVerticalLooksAtTheBottomEdge()
    {
        var v = new GraphViewport();
        v.SetViewSize(1000, 500);
        v.SetVertical(true);
        v.SetContentSize(1200, 5000);
        var old = new Box(Kind.Step, 24, 100, 200, 46, 0, "main", 0, 0, "a", null);
        v.Reveal(old, 5000);
        Assert.False(v.Following);
        Assert.True(v.VisibleRect().Y <= 100 && 146 <= v.VisibleRect().Bottom);
        var last = new Box(Kind.Step, 24, 4954, 200, 46, 0, "main", 9, 0, "z", null);
        v.Reveal(last, 5000);
        Assert.True(v.Following);
    }

    [Fact]
    public void ScreenAndWorldCoordinatesAreInverse()
    {
        GraphViewport v = NewViewport();
        v.ScrollBy(-1234, 0);
        v.SetZoom(0.75, 100, 50);
        Assert.Equal(321, v.ToWorldX(v.ToScreenX(321)), Tolerance);
        Assert.Equal(77, v.ToWorldY(v.ToScreenY(77)), Tolerance);
    }
}
