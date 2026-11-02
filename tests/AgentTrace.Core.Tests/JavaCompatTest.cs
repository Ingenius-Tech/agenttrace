namespace AgentTrace.Core.Tests;

public sealed class JavaCompatTest
{
    // Values computed with "...".hashCode() in Java.
    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 97)]
    [InlineData("hello", 99162322)]
    [InlineData("AgentTrace", 800699744)]
    [InlineData("src/Ñandú.java", -1802272121)] // lang-ok
    [InlineData("zzzzzzzzzzzz", 1097476992)]
    public void StringHashMatchesJavaHashCode(string text, int expected)
    {
        Assert.Equal(expected, JavaCompat.StringHash(text));
    }

    [Fact]
    public void StringHashHashesUtf16CodeUnitsSoSurrogatePairsMatchJava()
    {
        Assert.Equal(54959989, JavaCompat.StringHash("😀x"));
    }

    [Fact]
    public void StringHashOverflowsLikeAJavaInt()
    {
        // "zzzzzzzzzzzz" overflows several times: the result is still a 32-bit int.
        Assert.Equal(1097476992, JavaCompat.StringHash("zzzzzzzzzzzz"));
    }

    [Fact]
    public void StringHashIsStableAcrossCalls()
    {
        Assert.Equal(JavaCompat.StringHash("stable"), JavaCompat.StringHash("stable"));
    }

    [Theory]
    [InlineData(-5, 28, 15)]
    [InlineData(-1, 1, int.MaxValue)]
    [InlineData(int.MinValue, 31, 1)]
    [InlineData(1097476992, 16, 16746)]
    [InlineData(-1802272121, 16, 38035)]
    [InlineData(97, 16, 0)]
    public void UnsignedShiftRightMatchesTheJavaOperator(int value, int count, int expected)
    {
        Assert.Equal(expected, JavaCompat.UnsignedShiftRight(value, count));
    }
}
