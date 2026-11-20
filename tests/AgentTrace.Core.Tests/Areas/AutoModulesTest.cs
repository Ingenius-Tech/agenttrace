using AgentTrace.Core.Areas;

namespace AgentTrace.Core.Tests.Areas;

public sealed class AutoModulesTest
{
    private static readonly FilePath Root = new(OperatingSystem.IsWindows() ? @"C:\repo" : "/repo");

    private static readonly string[] Manifests = ["*.csproj", "package.json"];

    private static AreaClassifier Classifier(AreaRules rules) => new(Root, rules);

    private static string? ModuleOf(AreaClassifier c, string relative) => c.Classify(Root.Combine(relative.Split('/'))).Module;

    [Fact]
    public void AFolderWithAManifestIsAModuleNamedAfterItsFolder()
    {
        AreaRules rules = AutoModules.Build(["src/Core/Core.csproj", "src/Core/A.cs", "web/package.json"], Manifests, "repo");
        Assert.Contains(rules.Areas, a => a.Id == "src/Core" && a.Name == "Core" && a.Dimension == AreaDimension.Module);
        Assert.Contains(rules.Areas, a => a.Id == "web" && a.Name == "web");
        AreaClassifier c = Classifier(rules);
        Assert.Equal("src/Core", ModuleOf(c, "src/Core/A.cs"));
        Assert.Equal("web", ModuleOf(c, "web/deep/x.ts"));
    }

    [Fact]
    public void FilesUnderNoManifestFolderBelongToTheRootModule()
    {
        AreaRules rules = AutoModules.Build(["README.md", "docs/a.md", "src/Core/Core.csproj"], Manifests, "my-repo");
        AreaRule root = rules.Areas[^1];
        Assert.Equal((AutoModules.RootId, "my-repo"), (root.Id, root.Name));
        Assert.Equal(AutoModules.RootId, ModuleOf(Classifier(rules), "docs/a.md"));
        Assert.Equal(AutoModules.RootId, ModuleOf(Classifier(rules), "README.md"));
    }

    [Fact]
    public void ARepositoryWithoutManifestsIsOneRootModule()
    {
        AreaRules rules = AutoModules.Build(["a.txt", "b/c.txt"], Manifests, "r");
        Assert.Single(rules.Areas);
    }

    [Theory]
    [InlineData("we*b")]
    [InlineData("we?b")]
    public void AFolderNamedWithWildcardCharactersIsNotAModule(string folder)
    {
        AreaRules rules = AutoModules.Build([folder + "/package.json", "web/package.json", "wexb/a.js"], Manifests, "r");
        Assert.Equal(["web", AutoModules.RootId], rules.Areas.Select(a => a.Id).ToArray());
        Assert.Equal(AutoModules.RootId, ModuleOf(Classifier(rules), "wexb/a.js"));
    }

    [Fact]
    public void ARootManifestAddsNoModuleOfItsOwn()
    {
        AreaRules rules = AutoModules.Build(["package.json", "index.js"], Manifests, "r");
        Assert.Equal([AutoModules.RootId], rules.Areas.Select(a => a.Id).ToArray());
    }

    [Fact]
    public void TheNearestManifestFolderWins()
    {
        AreaRules rules = AutoModules.Build(["pkg/package.json", "pkg/sub/package.json", "pkg/sub/a.js", "pkg/b.js"], Manifests, "r");
        AreaClassifier c = Classifier(rules);
        Assert.Equal("pkg/sub", ModuleOf(c, "pkg/sub/a.js"));
        Assert.Equal("pkg", ModuleOf(c, "pkg/b.js"));
    }

    [Fact]
    public void ManifestPatternsAreMatchedAgainstTheFileNameOnly()
    {
        AreaRules rules = AutoModules.Build(["x/not-package.json", "y/a.csproj", "z/package.json.bak"], Manifests, "r");
        Assert.Equal(["y", AutoModules.RootId], rules.Areas.Select(a => a.Id).ToArray());
    }

    [Fact]
    public void AFolderThatCannotBeWrittenAsAGlobIsNotAModule()
    {
        AreaRules rules = AutoModules.Build(["app/[id]/package.json", "ok/package.json"], Manifests, "r");
        Assert.Equal(["ok", AutoModules.RootId], rules.Areas.Select(a => a.Id).ToArray());
    }

    [Fact]
    public void TheOrderIsDeterministic()
    {
        string[] paths = ["b/package.json", "a/package.json", "a/x/package.json"];
        AreaRules one = AutoModules.Build(paths, Manifests, "r");
        AreaRules two = AutoModules.Build(paths.AsEnumerable().Reverse(), Manifests, "r");
        Assert.Equal(one, two);
        Assert.Equal(["a/x", "a", "b", AutoModules.RootId], one.Areas.Select(a => a.Id).ToArray());
    }
}
