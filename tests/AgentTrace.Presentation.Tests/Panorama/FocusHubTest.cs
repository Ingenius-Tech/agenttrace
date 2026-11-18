using AgentTrace.Core;
using AgentTrace.Presentation.Panorama;

namespace AgentTrace.Presentation.Tests.Panorama;

/// <summary>When a published focus is applied: only while active, only as a change, never while applying another one.</summary>
public sealed class FocusHubTest
{
    private static readonly TraceFocus Step = TraceFocus.ForStep("c1");

    [Fact]
    public void NothingIsAppliedWhileTheHubIsNotActive()
    {
        var hub = new FocusHub();
        int applied = 0;
        Assert.False(hub.Publish(Step, _ => applied++));
        Assert.Equal(0, applied);
        Assert.Null(hub.Current);
    }

    [Fact]
    public void AFocusEqualToTheCurrentOneIsIgnoredAndADifferentOneIsApplied()
    {
        var hub = new FocusHub { Active = true };
        List<TraceFocus?> applied = [];
        Assert.True(hub.Publish(Step, applied.Add));
        Assert.False(hub.Publish(TraceFocus.ForStep("c1"), applied.Add));
        Assert.True(hub.Publish(TraceFocus.ForFile(new FilePath("a.cs")), applied.Add));
        Assert.Equal(2, applied.Count);
        Assert.Equal(FocusOrigin.Knowledge, hub.Current!.Origin);
    }

    [Fact]
    public void ClearingIsAChangeAndClearingTwiceIsNot()
    {
        var hub = new FocusHub { Active = true };
        int applied = 0;
        hub.Publish(Step, _ => applied++);
        Assert.True(hub.Publish(null, f => { Assert.Null(f); applied++; }));
        Assert.False(hub.Publish(null, _ => applied++));
        Assert.Equal(2, applied);
        Assert.Null(hub.Current);
    }

    [Fact]
    public void AFocusPublishedWhileApplyingAnotherOneIsIgnoredSoALinkNeverFeedsBack()
    {
        var hub = new FocusHub { Active = true };
        int applied = 0;
        hub.Publish(Step, _ =>
        {
            applied++;
            // A consumer that wrongly reported a selection of its own while being updated
            Assert.False(hub.Publish(TraceFocus.ForFile(new FilePath("x.cs")), _ => applied++));
        });
        Assert.Equal(1, applied);
        Assert.Equal(Step, hub.Current);
        Assert.False(hub.Applying);
    }

    [Fact]
    public void TheHubRecoversWhenApplyingThrows()
    {
        var hub = new FocusHub { Active = true };
        Assert.Throws<InvalidOperationException>(() => hub.Publish(Step, _ => throw new InvalidOperationException()));
        Assert.False(hub.Applying);
        Assert.True(hub.Publish(TraceFocus.ForGraphTurn(1), _ => { }));
    }

    [Fact]
    public void ThePanesThatReceiveAFocusAreAllButItsOrigin()
    {
        Assert.Equal([FocusOrigin.Areas, FocusOrigin.Knowledge], FocusHub.ConsumersOf(FocusOrigin.Graph));
        Assert.Equal([FocusOrigin.Graph, FocusOrigin.Knowledge], FocusHub.ConsumersOf(FocusOrigin.Areas));
        Assert.Equal([FocusOrigin.Graph, FocusOrigin.Areas], FocusHub.ConsumersOf(FocusOrigin.Knowledge));
    }

    [Fact]
    public void ClearForgetsTheCurrentFocusWithoutApplyingAnything()
    {
        var hub = new FocusHub { Active = true };
        hub.Publish(Step, _ => { });
        hub.Clear();
        Assert.Null(hub.Current);
        Assert.True(hub.Publish(Step, _ => { }), "the same selection can be linked again after a clear");
    }
}
