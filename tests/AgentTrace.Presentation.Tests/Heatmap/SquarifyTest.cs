using AgentTrace.Presentation.Heatmap;
using Rect = AgentTrace.Presentation.Graph.GraphGeometry.Rect;

namespace AgentTrace.Presentation.Tests.Heatmap;

public sealed class SquarifyTest
{
    private const double Eps = 1e-9;

    private static void AssertRect(Rect expected, Rect actual)
    {
        Assert.Equal(expected.X, actual.X, Eps);
        Assert.Equal(expected.Y, actual.Y, Eps);
        Assert.Equal(expected.W, actual.W, Eps);
        Assert.Equal(expected.H, actual.H, Eps);
    }

    [Fact]
    public void ThePaperExampleGivesItsFirstRows()
    {
        double[] weights = [6, 6, 4, 3, 2, 2, 1];
        IReadOnlyList<Rect> r = Squarify.Layout(weights, new Rect(0, 0, 6, 4));
        // First row: 6 and 6 in a column of width 3
        AssertRect(new Rect(0, 0, 3, 2), r[0]);
        AssertRect(new Rect(0, 2, 3, 2), r[1]);
        // Second: 4 and 3 in a strip of height 7/3 at the top of the rest
        AssertRect(new Rect(3, 0, 12.0 / 7, 7.0 / 3), r[2]);
        AssertRect(new Rect(3 + 12.0 / 7, 0, 9.0 / 7, 7.0 / 3), r[3]);
        AssertProportionalAndDisjoint(weights, r, new Rect(0, 0, 6, 4));
    }

    [Fact]
    public void AreasAreProportionalAndDoNotOverlap()
    {
        var random = new Random(42);
        double[] weights = [.. Enumerable.Range(0, 300).Select(_ => Math.Pow(random.NextDouble(), 3) * 1000 + 1).OrderByDescending(w => w)];
        var bounds = new Rect(10, 20, 640, 480);
        AssertProportionalAndDisjoint(weights, Squarify.Layout(weights, bounds), bounds);
    }

    [Fact]
    public void TheOrderIsStable()
    {
        double[] weights = [5, 3, 3, 3, 1];
        IReadOnlyList<Rect> a = Squarify.Layout(weights, new Rect(0, 0, 100, 60));
        IReadOnlyList<Rect> b = Squarify.Layout(weights, new Rect(0, 0, 100, 60));
        Assert.Equal(a, b);
        // At equal weight, the one that comes first in the input stays first (higher up or further left)
        Assert.True(a[1].X < a[2].X || a[1].Y < a[2].Y);
        Assert.True(a[2].X < a[3].X || a[2].Y < a[3].Y);
    }

    [Fact]
    public void ZeroWeightsAndEmptyBoundsGiveEmptyRectangles()
    {
        IReadOnlyList<Rect> r = Squarify.Layout([4, 0], new Rect(0, 0, 10, 10));
        Assert.Equal(100, r[0].W * r[0].H, Eps);
        Assert.Equal(0, r[1].W * r[1].H);
        Assert.All(Squarify.Layout([1, 1], new Rect(0, 0, 0, 10)), x => Assert.Equal(0, x.W * x.H));
        Assert.Empty(Squarify.Layout([], new Rect(0, 0, 10, 10)));
    }

    [Fact]
    public void ASingleWeightFillsTheBounds()
    {
        AssertRect(new Rect(1, 2, 30, 40), Squarify.Layout([7], new Rect(1, 2, 30, 40))[0]);
    }

    private static void AssertProportionalAndDisjoint(IReadOnlyList<double> weights, IReadOnlyList<Rect> rects, Rect bounds)
    {
        double scale = bounds.W * bounds.H / weights.Sum();
        for (int i = 0; i < rects.Count; i++)
        {
            Assert.Equal(weights[i] * scale, rects[i].W * rects[i].H, 1e-6);
            Assert.True(rects[i].X >= bounds.X - Eps && rects[i].Y >= bounds.Y - Eps);
            Assert.True(rects[i].Right <= bounds.Right + 1e-6 && rects[i].Bottom <= bounds.Bottom + 1e-6);
            for (int j = i + 1; j < rects.Count; j++)
            {
                double w = Math.Min(rects[i].Right, rects[j].Right) - Math.Max(rects[i].X, rects[j].X);
                double h = Math.Min(rects[i].Bottom, rects[j].Bottom) - Math.Max(rects[i].Y, rects[j].Y);
                Assert.False(w > 1e-6 && h > 1e-6, "cells " + i + " and " + j + " overlap");
            }
        }
    }
}
