using System.Text.Json;

namespace AgentTrace.Adapters.ClaudeCode.Tests;

/// <summary>
/// New tests (they do not exist in Java) for the <see cref="JsonPath"/> extension. The expected results were obtained
/// by running Jackson (<c>readTree</c>, <c>toString</c>, <c>asText</c>, <c>asLong</c>, <c>asBoolean</c>, <c>canConvertToLong</c>)
/// on the same inputs.
/// </summary>
public sealed class JsonPathTest
{
    private static string Compact(string json)
    {
        using JsonDocument? doc = JsonPath.Parse(json);
        return (doc is null ? default : doc.RootElement).ToJsonString();
    }

    [Fact]
    public void ToJsonStringKeepsKeyOrderAndRemovesWhitespace()
    {
        Assert.Equal("""{"b":1,"a":[1,2,{"c":null}],"s":"x"}""", Compact("""{ "b" : 1, "a": [ 1, 2, { "c": null } ], "s": "x" }"""));
        Assert.Equal("""{"x":[[],{}]}""", Compact("""{"x":[[],{}]}"""));
    }

    [Fact]
    public void RepeatedKeysKeepTheFirstPositionAndTheLastValueLikeAnObjectNode()
    {
        Assert.Equal("""{"k":4,"z":3}""", Compact("""{"k":1,"k":2,"z":3,"k":4}"""));
    }

    [Fact]
    public void StringsAreEscapedLikeJackson()
    {
        // only quotes, backslash and control characters; U+007F, the Unicode line and paragraph separators, "/" and non-ASCII go as is.
        // @LS@ and @PS@ are replaced by the JSON escape of those two separators and by the characters themselves.
        string input = """{"s":"tab\there\nnl\rcr\bbs\ffeed \u0001 \u001f \u007f é ñ … @LS@ @PS@ \/ \" \\ 😀 é"}""" // lang-ok
            .Replace("@LS@", "\\u" + "2028").Replace("@PS@", "\\u" + "2029");
        string expected = "{\"s\":\"tab\\there\\nnl\\rcr\\bbs\\ffeed \\u0001 \\u001F " + (char)0x7f + " é ñ … " + (char)0x2028 + " " + (char)0x2029 // lang-ok
            + " / \\\" \\\\ 😀 é\"}"; // lang-ok
        Assert.Equal(expected, Compact(input));
        Assert.Equal("""{"é\n":true,"":false,"a\"b":1}""", Compact("""{"é\n":true,"":false,"a\"b":1}""")); // lang-ok
    }

    [Fact]
    public void NumbersAreWrittenLikeJavaDoubleToString()
    {
        Assert.Equal(
            """{"a":1.0,"b":1.5,"c":1000.0,"d":1.0E-5,"e":1.23456789123E8,"f":0.001,"g":1.0E-4,"h":1.0E7,"i":9999999.999,"j":-0.0,"k":12345678901234567890,"l":1.0E22,"m":0.1,"n":100.0,"o":3.14159,"p":-1.5E-10,"q":"Infinity","r":1.23456789E8,"s":1.0E-300,"t":2.5E20,"u":4.35,"v":1.7976931348623157E308}""",
            Compact("""{"a":1.0,"b":1.50,"c":1e3,"d":1E-5,"e":123456789.123,"f":0.001,"g":0.0001,"h":1e7,"i":9999999.999,"j":-0.0,"k":12345678901234567890,"l":1.0E22,"m":0.1,"n":100.0,"o":3.14159,"p":-1.5e-10,"q":1e400,"r":123456.789e3,"s":1e-300,"t":2.5e+20,"u":4.35,"v":1.7976931348623157E308}"""));
        Assert.Equal("""{"a":0,"b":0,"c":-0.0,"d":-5}""", Compact("""{"a":-0,"b":0,"c":-0.0,"d":-5}"""));
    }

    [Fact]
    public void ParseReadsTheFirstValueAndIgnoresWhatFollowsLikeReadTree()
    {
        Assert.Equal("""{"a":1}""", Compact("""{"a":1} trailing"""));
        Assert.Equal("""{"a":[1,2]}""", Compact("""{"a":[1,2]}{"b":2}"""));
        Assert.Null(JsonPath.Parse(""));
        Assert.Null(JsonPath.Parse("   "));
        Assert.ThrowsAny<JsonException>(() => JsonPath.Parse("{"));
        Assert.ThrowsAny<JsonException>(() => JsonPath.Parse("nope"));
    }

    [Fact]
    public void MissingNodesBehaveLikeJacksonsMissingNode()
    {
        JsonElement missing = default;
        Assert.True(missing.IsMissing());
        Assert.Equal("", missing.ToJsonString());
        Assert.Equal("", missing.AsText());
        Assert.Equal("d", missing.AsText("d"));
        Assert.Equal(7, missing.AsLong(7));
        Assert.False(missing.AsBoolean(false));
        Assert.Empty(missing.Elements());
        Assert.True(missing.Path("x").IsMissing());
        using JsonDocument doc = JsonDocument.Parse("""{"a":[1],"n":null}""");
        Assert.True(doc.RootElement.Path("a").Path("x").IsMissing());
        Assert.True(doc.RootElement.Path("n").IsNull());
        Assert.Equal("d", doc.RootElement.Path("n").AsText("d"));
        Assert.Equal("null", doc.RootElement.Path("n").AsText());
    }

    [Fact]
    public void ObjectsIterateTheirValuesAndArraysTheirElements()
    {
        using JsonDocument obj = JsonDocument.Parse("""{"a":1,"b":"x"}""");
        Assert.Equal(new[] { "1", "x" }, obj.RootElement.Elements().Select(e => e.AsText()).ToArray());
        using JsonDocument arr = JsonDocument.Parse("""[3,4]""");
        Assert.Equal(new[] { "3", "4" }, arr.RootElement.Elements().Select(e => e.AsText()).ToArray());
        Assert.Equal(2, arr.RootElement.Size());
    }

    [Theory]
    // input, asText(), asLong(7), asBoolean(false), canConvertToLong()
    [InlineData("\"abc\"", "abc", 7L, false, false)]
    [InlineData("5", "5", 5L, true, true)]
    [InlineData("5.5", "5.5", 5L, false, true)]
    [InlineData("1e2", "100.0", 100L, false, true)]
    [InlineData("[1]", "", 7L, false, false)]
    [InlineData("{\"a\":1}", "", 7L, false, false)]
    [InlineData("\"12\"", "12", 12L, false, false)]
    [InlineData("\" 12 \"", " 12 ", 12L, false, false)]
    [InlineData("\"1.9\"", "1.9", 1L, false, false)]
    [InlineData("\"-3\"", "-3", -3L, false, false)]
    [InlineData("\"+4\"", "+4", 4L, false, false)]
    [InlineData("\"x\"", "x", 7L, false, false)]
    [InlineData("9223372036854775807", "9223372036854775807", long.MaxValue, true, true)]
    [InlineData("9223372036854775808", "9223372036854775808", long.MinValue, true, false)]
    [InlineData("1e19", "1.0E19", long.MaxValue, false, false)]
    [InlineData("-1e19", "-1.0E19", long.MinValue, false, false)]
    [InlineData("12345678901234567890123", "12345678901234567890123", 4807115922877859019L, true, false)]
    [InlineData("\"true\"", "true", 7L, true, false)]
    [InlineData("\" false \"", " false ", 7L, false, false)]
    [InlineData("0", "0", 0L, false, true)]
    [InlineData("-7.9", "-7.9", -7L, false, true)]
    [InlineData("1e18", "1.0E18", 1_000_000_000_000_000_000L, false, true)]
    [InlineData("true", "true", 1L, true, false)]
    [InlineData("false", "false", 0L, false, false)]
    public void ScalarConversionsMatchJackson(string json, string text, long asLong, bool asBoolean, bool canConvert)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement n = doc.RootElement;
        Assert.Equal(text, n.AsText());
        Assert.Equal(text, n.AsText("d"));
        Assert.Equal(asLong, n.AsLong(7));
        Assert.Equal(asBoolean, n.AsBoolean(false));
        Assert.Equal(canConvert, n.CanConvertToLong());
    }
}
