namespace AgentTrace.Core.Tests;

public sealed class OrderedCollectionsTest
{
    [Fact]
    public void AnOrderedSetIteratesInInsertionOrderAndIgnoresDuplicates()
    {
        var set = new OrderedSet<string>();

        Assert.True(set.Add("z"));
        Assert.True(set.Add("a"));
        Assert.False(set.Add("z"));
        Assert.True(set.Add("m"));

        Assert.Equal(["z", "a", "m"], set.ToArray());
        Assert.Equal(3, set.Count);
        Assert.True(set.Contains("a"));
    }

    [Fact]
    public void AnOrderedSetCanBeClearedAndReused()
    {
        var set = new OrderedSet<int>();
        set.Add(3);
        set.Add(1);

        set.Clear();
        set.Add(2);
        set.Add(3);

        Assert.Equal([2, 3], set.ToArray());
    }

    [Fact]
    public void AnOrderedMapIteratesInInsertionOrderAndKeepsThePositionOnReassign()
    {
        var map = new OrderedMap<string, int>();
        map["b"] = 1;
        map["a"] = 2;
        map["b"] = 3;
        map["c"] = 4;

        Assert.Equal(["b", "a", "c"], map.Entries.Select(e => e.Key).ToArray());
        Assert.Equal([3, 2, 4], map.Values.ToArray());
        Assert.Equal(3, map.Count);
    }

    [Fact]
    public void GetOrAddCreatesOnlyOnce()
    {
        var map = new OrderedMap<string, List<int>>();
        int created = 0;

        map.GetOrAdd("k", _ => { created++; return []; }).Add(1);
        map.GetOrAdd("k", _ => { created++; return []; }).Add(2);

        Assert.Equal(1, created);
        Assert.Equal([1, 2], map["k"]);
        Assert.True(map.TryGetValue("k", out _));
        Assert.False(map.TryGetValue("x", out _));
    }
}
