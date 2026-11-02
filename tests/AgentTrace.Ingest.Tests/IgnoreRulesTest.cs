using AgentTrace.Testing;
using AgentTrace.Core;

namespace AgentTrace.Ingest.Tests;

public sealed class IgnoreRulesTest
{
    private static readonly FilePath Root = new("project");

    private static FilePath P(string relative) => Root.Combine(relative);

    [Fact]
    public void DotGitIsAlwaysIgnoredEvenWithGitRules()
    {
        IgnoreRules withGit = IgnoreRules.WithGit(Root, []);
        Assert.True(withGit.IsIgnored(P(".git")));
        Assert.True(withGit.IsIgnored(P(".git/config")));
        Assert.True(IgnoreRules.WithoutGit(Root).IsIgnored(P("sub/.git/HEAD")));
    }

    [Fact]
    public void GitIgnoredFoldersIncludeEverythingBelowThem()
    {
        IgnoreRules rules = IgnoreRules.WithGit(Root, [P("build"), P("notes.log")]);
        Assert.True(rules.IsIgnored(P("build")));
        Assert.True(rules.IsIgnored(P("build/classes/A.class")));
        Assert.True(rules.IsIgnored(P("notes.log")));
        Assert.False(rules.IsIgnored(P("src/A.java")));
        Assert.False(rules.IsIgnored(P("buildings/x.txt"))); // only the exact folder, not those starting the same
    }

    [Fact]
    public void WithGitTheFixedListNoLongerApplies()
    {
        IgnoreRules rules = IgnoreRules.WithGit(Root, []);
        Assert.False(rules.IsIgnored(P("node_modules/x.js"))); // if git does not ignore it, it is shown
        Assert.False(rules.IsIgnored(P("target/classes")));
    }

    [Fact]
    public void WithoutGitTheFixedListApplies()
    {
        IgnoreRules rules = IgnoreRules.WithoutGit(Root);
        Assert.True(rules.IsIgnored(P("node_modules")));
        Assert.True(rules.IsIgnored(P("a/target/classes/B.class")));
        Assert.True(rules.IsIgnored(P(".venv/lib")));
        Assert.False(rules.IsIgnored(P("src/main")));
        Assert.False(rules.IsIgnored(P(".gitignore"))); // dotfiles are shown
    }

    [Fact]
    public void TheProjectRootItselfIsNeverIgnored()
    {
        Assert.False(IgnoreRules.WithoutGit(Root).IsIgnored(Root));
        Assert.False(IgnoreRules.WithGit(Root, [Root]).IsIgnored(Root));
    }
}
