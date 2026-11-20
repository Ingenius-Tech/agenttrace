using AgentTrace.Presentation.Observation;

namespace AgentTrace.Presentation.Tests.Observation;

/// <summary>One background pass at a time: a failing pass frees the slot, and a cancelled one never delivers its result.</summary>
public sealed class SinglePassTest
{
    private static readonly Action<Action> Inline = a => a();

    [Fact]
    public async Task APassDeliversItsResultAndFreesTheSlot()
    {
        var pass = new SinglePass();
        int delivered = 0;
        Task task = pass.TryRun(() => 7, v => delivered = v, Inline)!;
        Assert.True(pass.Busy);
        Assert.Null(pass.TryRun(() => 1, _ => { }, Inline)); // only one at a time
        await task;
        Assert.False(pass.Busy);
        Assert.Equal(7, delivered);
    }

    [Fact]
    public async Task AFailingPassFreesTheSlotAndDeliversNothing()
    {
        var pass = new SinglePass();
        bool delivered = false;
        await pass.TryRun<int>(() => throw new IOException("boom"), _ => delivered = true, Inline)!;
        Assert.False(pass.Busy);
        Assert.False(delivered);
        Assert.NotNull(pass.TryRun(() => 1, _ => { }, Inline)); // the next one runs
    }

    [Fact]
    public async Task ACancelledPassNeverDeliversAndDoesNotFreeTheNextOnesSlot()
    {
        var pass = new SinglePass();
        var release = new ManualResetEventSlim();
        bool delivered = false;
        Task old = pass.TryRun(() => { release.Wait(); return 1; }, _ => delivered = true, Inline)!;
        pass.Cancel();
        Assert.False(pass.Busy);
        Task next = pass.TryRun(() => { release.Wait(); return 2; }, _ => { }, Inline)!;
        release.Set();
        await Task.WhenAll(old, next);
        Assert.False(delivered);
        Assert.False(pass.Busy);
    }
}
