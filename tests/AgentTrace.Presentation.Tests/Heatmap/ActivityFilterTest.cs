using AgentTrace.Presentation.Heatmap;

namespace AgentTrace.Presentation.Tests.Heatmap;

public sealed class ActivityFilterTest
{
    [Fact]
    public void ParseReadsTheListedTypes()
    {
        Assert.Equal(new ActivityFilter(true, false, true), ActivityFilter.Parse("reads,errors"));
        Assert.Equal(new ActivityFilter(false, true, false), ActivityFilter.Parse(" WRITES "));
        Assert.Equal(new ActivityFilter(true, true, false), ActivityFilter.Parse("lecturas;escrituras")); // lang-ok: Spanish aliases are still accepted
    }

    [Fact]
    public void WithoutAnyValidNameEverythingIsActive()
    {
        Assert.Equal(ActivityFilter.All, ActivityFilter.Parse(null));
        Assert.Equal(ActivityFilter.All, ActivityFilter.Parse(""));
        Assert.Equal(ActivityFilter.All, ActivityFilter.Parse("nothing,other"));
        Assert.Equal(3, ActivityFilter.All.Count);
        Assert.Equal(1, ActivityFilter.Parse("errors").Count);
    }

    [Fact]
    public void FileActivityValueAddsTheActiveTypes()
    {
        var a = new FileActivity(Reads: 2, Created: 1, Modified: 3, Deleted: 0, External: 1, Errors: 4);
        Assert.Equal(5, a.Writes);
        Assert.Equal(11, a.Total);
        Assert.Equal(11, a.Value(ActivityFilter.All));
        Assert.Equal(2, a.Value(new ActivityFilter(true, false, false)));
        Assert.Equal(9, a.Value(new ActivityFilter(false, true, true)));
        Assert.Equal(22, a.Plus(a).Total);
    }
}
