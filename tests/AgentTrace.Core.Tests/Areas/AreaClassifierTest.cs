using AgentTrace.Core.Areas;

namespace AgentTrace.Core.Tests.Areas;

public sealed class AreaClassifierTest
{
    private static readonly FilePath Root = new(OperatingSystem.IsWindows() ? @"C:\repo" : "/repo");

    private static FilePath F(string relative) => Root.Combine(relative.Split('/'));

    private static AreaRule Rule(string id, AreaDimension dimension, string[] include, string[]? exclude = null) =>
        new(id, id, dimension, include.ToValueList(), (exclude ?? []).ToValueList());

    private static AreaClassifier Classifier(params AreaRule[] rules) => new(Root, new AreaRules(rules.ToValueList()));

    [Fact]
    public void EachDimensionIsClassifiedIndependently()
    {
        AreaClassifier c = Classifier(
            Rule("data", AreaDimension.Layer, ["src/Data/**"]),
            Rule("billing", AreaDimension.Module, ["src/Billing/**", "src/Data/Billing*"]),
            Rule("test", AreaDimension.Kind, ["*Tests.cs"]));
        Assert.Equal(new AreaAssignment("data", "billing", "test"), c.Classify(F("src/Data/BillingTests.cs")));
        Assert.Equal(new AreaAssignment("data", null, null), c.Classify(F("src/Data/Repo.cs")));
        Assert.Equal(new AreaAssignment(null, "billing", null), c.Classify(F("src/Billing/Invoice.cs")));
    }

    [Fact]
    public void TheFirstMatchingAreaOfADimensionWins()
    {
        AreaClassifier c = Classifier(
            Rule("generated", AreaDimension.Layer, ["src/Api/Generated/**"]),
            Rule("api", AreaDimension.Layer, ["src/Api/**"]));
        Assert.Equal("generated", c.Classify(F("src/Api/Generated/A.cs")).Layer);
        Assert.Equal("api", c.Classify(F("src/Api/B.cs")).Layer);

        AreaClassifier swapped = Classifier(
            Rule("api", AreaDimension.Layer, ["src/Api/**"]),
            Rule("generated", AreaDimension.Layer, ["src/Api/Generated/**"]));
        Assert.Equal("api", swapped.Classify(F("src/Api/Generated/A.cs")).Layer);
    }

    [Fact]
    public void ExcludeRemovesWhatIncludeMatched()
    {
        AreaClassifier c = Classifier(
            Rule("app", AreaDimension.Layer, ["src/App/**"], ["**/*.generated.cs"]),
            Rule("rest", AreaDimension.Layer, ["src/**"]));
        Assert.Equal("app", c.Classify(F("src/App/A.cs")).Layer);
        Assert.Equal("rest", c.Classify(F("src/App/A.generated.cs")).Layer);
    }

    [Fact]
    public void AFolderPatternClassifiesEverythingBelowIt()
    {
        AreaClassifier c = Classifier(Rule("docs", AreaDimension.Kind, ["docs"]));
        Assert.Equal("docs", c.Classify(F("docs/a/b/c.md")).Kind);
        Assert.Equal("docs", c.Classify(F("src/docs/c.md")).Kind);
        Assert.Null(c.Classify(F("src/documents/c.md")).Kind);
    }

    [Fact]
    public void AFileOutsideTheRepositoryOrTheRootItselfHasNoArea()
    {
        AreaClassifier c = Classifier(Rule("all", AreaDimension.Module, ["**"]));
        FilePath outside = new(OperatingSystem.IsWindows() ? @"C:\elsewhere\a.cs" : "/elsewhere/a.cs");
        Assert.Equal(AreaAssignment.None, c.Classify(outside));
        Assert.Equal(AreaAssignment.None, c.Classify(Root));
        Assert.Equal("all", c.Classify(F("a.cs")).Module);
    }

    [Fact]
    public void WithoutRulesNothingIsClassified()
    {
        Assert.Equal(AreaAssignment.None, new AreaClassifier(Root, AreaRules.Empty).Classify(F("a.cs")));
    }

    [Fact]
    public void ResultsAreCachedPerPath()
    {
        AreaClassifier c = Classifier(Rule("all", AreaDimension.Module, ["**"]));
        Assert.Same(c.Classify(F("a.cs")), c.Classify(F("a.cs")));
    }

    [Fact]
    public void OfReturnsTheIdOfTheDimension()
    {
        var a = new AreaAssignment("l", "m", "k");
        Assert.Equal("l", a.Of(AreaDimension.Layer));
        Assert.Equal("m", a.Of(AreaDimension.Module));
        Assert.Equal("k", a.Of(AreaDimension.Kind));
    }

    [Fact]
    public void AFolderOnlyPatternNeedsSomethingBelowTheFolder()
    {
        AreaClassifier c = Classifier(Rule("mig", AreaDimension.Kind, ["Migrations/"]));
        Assert.Equal("mig", c.Classify(F("src/Data/Migrations/001.cs")).Kind);
        Assert.Null(c.Classify(F("src/Data/Migrations")).Kind);
    }
}
