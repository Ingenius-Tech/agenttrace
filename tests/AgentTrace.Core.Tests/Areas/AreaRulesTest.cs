using AgentTrace.Core.Areas;

namespace AgentTrace.Core.Tests.Areas;

public sealed class AreaRulesTest
{
    private static RawAreaRule Raw(string id = "data", string name = "Data", string dimension = "layer", string[]? include = null,
        string[]? exclude = null, bool shared = false) => new(id, name, dimension, include ?? ["src/**"], exclude, shared);

    private static AreaRulesResult Validate(params RawAreaRule[] areas) => AreaRules.Validate(1, areas);

    private static AreaRuleError Single(AreaRulesResult result)
    {
        Assert.False(result.IsValid);
        Assert.Null(result.Rules);
        return Assert.Single(result.Errors);
    }

    [Fact]
    public void AValidFileProducesTheRulesInFileOrder()
    {
        AreaRulesResult r = Validate(Raw("data", dimension: "layer"), Raw("api", "Api", "module", ["api/**"], ["api/gen/**"], shared: true), Raw("test", "Tests", "kind"));
        Assert.True(r.IsValid);
        Assert.Empty(r.Errors);
        Assert.Equal(["data", "api", "test"], r.Rules!.Areas.Select(a => a.Id).ToArray());
        Assert.Equal([AreaDimension.Layer, AreaDimension.Module, AreaDimension.Kind], r.Rules.Areas.Select(a => a.Dimension).ToArray());
        Assert.Equal(["api/gen/**"], r.Rules.Areas[1].Exclude);
        Assert.True(r.Rules.Areas[1].Shared);
        Assert.Empty(r.Rules.Areas[0].Exclude);
    }

    [Fact]
    public void AnEmptyAreaListIsValid()
    {
        Assert.True(Validate().IsValid);
    }

    [Fact]
    public void AMissingVersionIsReported()
    {
        AreaRuleError e = Single(AreaRules.Validate(null, []));
        Assert.Equal(AreaRuleErrorKind.MissingField, e.Kind);
        Assert.Equal("version", e.Field);
    }

    [Fact]
    public void AnotherVersionIsUnsupported()
    {
        AreaRuleError e = Single(AreaRules.Validate(2, []));
        Assert.Equal(AreaRuleErrorKind.UnsupportedVersion, e.Kind);
        Assert.Equal("2", e.Field);
    }

    [Fact]
    public void MissingAreasIsReported()
    {
        AreaRuleError e = Single(AreaRules.Validate(1, null));
        Assert.Equal(AreaRuleErrorKind.MissingField, e.Kind);
        Assert.Equal("areas", e.Field);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("dimension")]
    [InlineData("include")]
    [InlineData("id")]
    public void AMissingRequiredFieldIsReportedWithItsName(string field)
    {
        RawAreaRule raw = field switch
        {
            "name" => Raw() with { Name = null },
            "dimension" => Raw() with { Dimension = null },
            "include" => Raw() with { Include = null },
            _ => Raw() with { Id = null },
        };
        AreaRuleError e = Single(Validate(raw));
        Assert.Equal(AreaRuleErrorKind.MissingField, e.Kind);
        Assert.Equal(field, e.Field);
        Assert.Equal(0, e.AreaIndex);
    }

    [Fact]
    public void AnUnknownDimensionIsReportedWithItsValue()
    {
        AreaRuleError e = Single(Validate(Raw(dimension: "tier")));
        Assert.Equal(AreaRuleErrorKind.UnknownDimension, e.Kind);
        Assert.Equal("tier", e.Field);
        Assert.Equal("data", e.AreaId);
    }

    [Theory]
    [InlineData("Data")]
    [InlineData("-data")]
    [InlineData("da ta")]
    [InlineData("data.x")]
    [InlineData("data\n")]
    public void AnInvalidIdIsReported(string id)
    {
        Assert.Equal(AreaRuleErrorKind.InvalidId, Single(Validate(Raw(id))).Kind);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("0data")]
    [InlineData("data-layer_2")]
    public void ValidIdsAreAccepted(string id)
    {
        Assert.True(Validate(Raw(id)).IsValid);
    }

    [Fact]
    public void AnIdIsUniqueAcrossDimensions()
    {
        AreaRuleError e = Single(Validate(Raw("x", dimension: "layer"), Raw("x", dimension: "module")));
        Assert.Equal(AreaRuleErrorKind.DuplicateId, e.Kind);
        Assert.Equal(1, e.AreaIndex);
        Assert.Equal("x", e.AreaId);
    }

    [Fact]
    public void AnEmptyIncludeIsReported()
    {
        Assert.Equal(AreaRuleErrorKind.EmptyInclude, Single(Validate(Raw(include: []))).Kind);
    }

    [Fact]
    public void AnInvalidGlobIsReportedWithThePatternAndTheReason()
    {
        AreaRuleError inInclude = Single(Validate(Raw(include: ["src/{a,b}"])));
        Assert.Equal(AreaRuleErrorKind.InvalidGlob, inInclude.Kind);
        Assert.Equal("src/{a,b}", inInclude.Pattern);
        Assert.Equal(GlobError.Braces, inInclude.GlobError);

        AreaRuleError inExclude = Single(Validate(Raw(exclude: ["!x"])));
        Assert.Equal(GlobError.Negation, inExclude.GlobError);
    }

    [Fact]
    public void AFileWithAnyErrorIsNeverAppliedPartially()
    {
        AreaRulesResult r = Validate(Raw("good"), Raw("bad", dimension: "tier"), Raw("also-good", dimension: "module"));
        Assert.False(r.IsValid);
        Assert.Null(r.Rules);
        Assert.Single(r.Errors);
    }

    [Fact]
    public void EveryErrorIsCollected()
    {
        AreaRulesResult r = AreaRules.Validate(3, [Raw("X"), Raw("a", include: ["a\\b"]), Raw("a")]);
        Assert.Equal(
            [AreaRuleErrorKind.UnsupportedVersion, AreaRuleErrorKind.InvalidId, AreaRuleErrorKind.InvalidGlob, AreaRuleErrorKind.DuplicateId],
            r.Errors.Select(e => e.Kind).ToArray());
    }

    [Fact]
    public void ThenAppendsTheOtherRules()
    {
        AreaRules a = Validate(Raw("one")).Rules!;
        AreaRules b = Validate(Raw("two", dimension: "kind")).Rules!;
        Assert.Equal(["one", "two"], a.Then(b).Areas.Select(x => x.Id).ToArray());
    }
}
