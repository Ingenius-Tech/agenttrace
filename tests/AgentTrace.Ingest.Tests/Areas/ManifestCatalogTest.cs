using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Ingest.Areas;

namespace AgentTrace.Ingest.Tests.Areas;

public sealed class ManifestCatalogTest
{
    private static readonly FilePath Root = new(OperatingSystem.IsWindows() ? @"C:\repo" : "/repo");

    [Fact]
    public void TheEmbeddedManifestPatternsAreValidGlobs()
    {
        Assert.NotEmpty(ManifestCatalog.Patterns);
        Assert.Contains("package.json", ManifestCatalog.Patterns);
        Assert.Contains("*.csproj", ManifestCatalog.Patterns);
        Assert.All(ManifestCatalog.Patterns, p => Assert.Null(GlobPattern.Validate(p)));
    }

    [Fact]
    public void TheEmbeddedPresetValidatesWithTheSameCodeAsAUserFile()
    {
        AreaRulesRead read = AreaRulesReader.Parse(
            new StreamReader(typeof(ManifestCatalog).Assembly.GetManifestResourceStream("AgentTrace.Ingest.Areas.generic-preset.json")!).ReadToEnd());
        Assert.Empty(read.Errors);
        Assert.NotNull(read.Rules);
        Assert.Equal(read.Rules, ManifestCatalog.GenericPreset);
    }

    [Fact]
    public void ThePresetHasKindsOnly()
    {
        Assert.All(ManifestCatalog.GenericPreset.Areas, a => Assert.Equal(AreaDimension.Kind, a.Dimension));
        Assert.Equal(["test", "docs", "migration", "contract", "dependencies", "config"], ManifestCatalog.GenericPreset.Areas.Select(a => a.Id).ToArray());
    }

    [Theory]
    [InlineData("tests/FooTests.cs", "test")]
    [InlineData("src/Foo.Tests/A.cs", "test")]
    [InlineData("web/app.spec.ts", "test")]
    [InlineData("docs/architecture/x.yml", "docs")]
    [InlineData("README.md", "docs")]
    [InlineData("src/Data/Migrations/001.cs", "migration")]
    [InlineData("api/orders.proto", "contract")]
    [InlineData("openapi.yaml", "contract")]
    [InlineData("web/package.json", "dependencies")]
    [InlineData("src/App/App.csproj", "dependencies")]
    [InlineData("Directory.Packages.props", "dependencies")]
    [InlineData(".github/ci.yml", "config")]
    [InlineData("Dockerfile", "config")]
    [InlineData("src/Program.cs", null)]
    public void ThePresetClassifiesTheKindOfCommonFiles(string relative, string? kind)
    {
        var classifier = new AreaClassifier(Root, ManifestCatalog.GenericPreset);
        Assert.Equal(kind, classifier.Classify(Root.Combine(relative.Split('/'))).Kind);
    }

    [Fact]
    public void TheFallbackCombinesModulesAndKindsWithoutLayers()
    {
        AreaRules rules = AreaFallback.Build(["src/Core/Core.csproj", "src/Core/A.cs", "src/Core/ATests.cs", "README.md"], "repo");
        var classifier = new AreaClassifier(Root, rules);
        Assert.Equal(new AreaAssignment(null, "src/Core", null), classifier.Classify(Root.Combine("src", "Core", "A.cs")));
        Assert.Equal(new AreaAssignment(null, "src/Core", "test"), classifier.Classify(Root.Combine("src", "Core", "ATests.cs")));
        Assert.Equal(new AreaAssignment(null, AutoModules.RootId, "docs"), classifier.Classify(Root.Combine("README.md")));
        Assert.DoesNotContain(rules.Areas, a => a.Dimension == AreaDimension.Layer);
    }
}
