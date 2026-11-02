using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Presentation.Graph;
using static AgentTrace.Core.AgentEvent;
using static AgentTrace.Core.Flow.FlowGraph;
using static AgentTrace.Presentation.Tests.Graph.GraphTestKit;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Graph;

public sealed class StepTextTest
{
    private static Step MakeStep(StepState state, DateTimeOffset? end) =>
        new("c", "Bash", ToolKind.Execute, "ls", "{}", state, null, T0, end, ValueList<FileTouch>.Empty, null);

    [Fact]
    public void FormatsDurations()
    {
        Assert.Equal("", StepText.FormatDuration(null));
        Assert.Equal("0 ms", StepText.FormatDuration(TimeSpan.FromMilliseconds(-5)));
        Assert.Equal("850 ms", StepText.FormatDuration(TimeSpan.FromMilliseconds(850)));
        Assert.Equal("4.9 s", StepText.FormatDuration(TimeSpan.FromMilliseconds(4_900)));
        Assert.Equal("1 min 5 s", StepText.FormatDuration(TimeSpan.FromSeconds(65)));
        Assert.Equal("1 h 2 min", StepText.FormatDuration(TimeSpan.FromMinutes(62)));
    }

    [Fact]
    public void StatusSaysTheStateInWordsNotJustColor()
    {
        Assert.Equal("4.9 s", StepText.Status(MakeStep(StepState.Ok, T0.AddMilliseconds(4900)), T0));
        Assert.Equal("ok", StepText.Status(MakeStep(StepState.Ok, null), T0));
        Assert.Equal("error · 2.0 s", StepText.Status(MakeStep(StepState.Error, T0.AddSeconds(2)), T0));
        Assert.Equal("running · 12.0 s", StepText.Status(MakeStep(StepState.Running, null), T0.AddSeconds(12)));
        Assert.Equal("no result", StepText.Status(MakeStep(StepState.NoResult, null), T0));
    }

    [Fact]
    public void StatusAndDurationsFollowTheSpanishCulture()
    {
        using var _ = CultureScope.Spanish();
        Assert.Equal("error · 2,0 s", StepText.Status(MakeStep(StepState.Error, T0.AddSeconds(2)), T0)); // lang-ok
        Assert.Equal("en curso · 12,0 s", StepText.Status(MakeStep(StepState.Running, null), T0.AddSeconds(12))); // lang-ok
        Assert.Equal("sin resultado", StepText.Status(MakeStep(StepState.NoResult, null), T0)); // lang-ok
    }

    /// <summary>Added cases: the half rounds up (like the HALF_UP of Java's String.format) and the separator comes from the culture.</summary>
    [Theory]
    [InlineData(1_050, "1.1 s")]
    [InlineData(1_150, "1.2 s")]
    [InlineData(1_049, "1.0 s")]
    [InlineData(59_950, "60.0 s")]
    [InlineData(1_000, "1.0 s")]
    public void SecondsRoundHalfUpAndUseTheDecimalPointOfTheEnglishCulture(int ms, string expected)
    {
        Assert.Equal(expected, StepText.FormatDuration(TimeSpan.FromMilliseconds(ms)));
    }

    [Fact]
    public void SecondsUseTheCommaOfTheSpanishCulture()
    {
        using var _ = CultureScope.Spanish();
        Assert.Equal("1,1 s", StepText.FormatDuration(TimeSpan.FromMilliseconds(1_050))); // lang-ok
    }
}
