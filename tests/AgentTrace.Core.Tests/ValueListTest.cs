namespace AgentTrace.Core.Tests;

public sealed class ValueListTest
{
    private sealed record Holder(ValueList<string> Items, int N);

    [Fact]
    public void ComparesBySequenceNotByReference()
    {
        Assert.Equal(ValueList.Of("a", "b"), ValueList.Of("a", "b"));
        Assert.NotEqual(ValueList.Of("a", "b"), ValueList.Of("b", "a"));
        Assert.NotEqual(ValueList.Of("a"), ValueList.Of("a", "b"));
        Assert.True(ValueList.Of("a").Equals(new[] { "a" }.ToValueList()));
    }

    [Fact]
    public void EqualListsHaveEqualHashCodes()
    {
        Assert.Equal(ValueList.Of(1, 2, 3).GetHashCode(), new List<int> { 1, 2, 3 }.ToValueList().GetHashCode());
    }

    [Fact]
    public void RecordsContainingAListCompareByContent()
    {
        Assert.Equal(new Holder(ValueList.Of("x"), 1), new Holder(ValueList.Of("x"), 1));
        Assert.NotEqual(new Holder(ValueList.Of("x"), 1), new Holder(ValueList.Of("y"), 1));
        Assert.Equal(new Holder(ValueList.Of("x"), 1).GetHashCode(), new Holder(ValueList.Of("x"), 1).GetHashCode());
    }

    [Fact]
    public void ElementsAreComparedWithTheirOwnEquality()
    {
        Assert.Equal(
            ValueList.Of(new FilePath("a.txt"), new FilePath("b.txt")),
            ValueList.Of(new FilePath("a.txt"), new FilePath("b/../b.txt")));
        Assert.Equal(ValueList.Of(ValueList.Of(1), ValueList.Of(2, 3)), ValueList.Of(ValueList.Of(1), ValueList.Of(2, 3)));
    }

    [Fact]
    public void IsImmutableEvenIfTheSourceChanges()
    {
        var source = new List<string> { "a" };
        var list = source.ToValueList();

        source.Add("b");

        Assert.Single(list);
    }

    [Fact]
    public void EmptyIsSharedAndEqualToAnyEmptyList()
    {
        Assert.Same(ValueList<string>.Empty, ValueList<string>.Empty);
        Assert.Equal(ValueList<string>.Empty, new List<string>().ToValueList());
        Assert.Empty(ValueList<int>.Empty);
    }

    [Fact]
    public void BehavesAsAReadOnlyList()
    {
        IReadOnlyList<string> list = ValueList.Of("a", "b", "c");

        Assert.Equal(3, list.Count);
        Assert.Equal("b", list[1]);
        Assert.Equal(["a", "b", "c"], list.ToArray());
    }

    [Fact]
    public void ToStringListsTheElements()
    {
        Assert.Equal("[a, b]", ValueList.Of("a", "b").ToString());
        Assert.Equal("[]", ValueList<string>.Empty.ToString());
    }

    [Fact]
    public void IsNeverEqualToNullOrAnotherType()
    {
        Assert.False(ValueList.Of("a").Equals(null));
        Assert.False(ValueList.Of("a").Equals((object)"a"));
    }
}
