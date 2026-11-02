using AgentTrace.Core;
using AgentTrace.Presentation.Tree;

namespace AgentTrace.Presentation.Tests.Tree;

public sealed class TreeExpansionTest
{
    private static readonly FilePath Root = new("project");

    private static FilePath P(string relative) => Root.Combine(relative);

    [Fact]
    public void ListsEveryFolderOnTheWayShallowestFirstWithoutRepeats()
    {
        IReadOnlyList<FilePath> dirs = TreeExpansion.DirectoriesToExpand(Root,
            [P("src/main/java/A.java"), P("src/main/B.java"), P("src/test/C.java"), P("README.md")]);
        Assert.Equal([Root, P("src"), P("src/main"), P("src/test"), P("src/main/java")], dirs);
    }

    [Fact]
    public void AFileAtTheRootOnlyNeedsTheRoot()
    {
        Assert.Equal([Root], TreeExpansion.DirectoriesToExpand(Root, [P("pom.xml")]));
    }

    [Fact]
    public void PathsOutsideTheRootAreIgnored()
    {
        FilePath outside = Root.Parent!.Value.Combine("otro/f.txt");
        Assert.Empty(TreeExpansion.DirectoriesToExpand(Root, [outside]));
    }

    [Fact]
    public void NothingToExpandForNoTargets()
    {
        Assert.Empty(TreeExpansion.DirectoriesToExpand(Root, []));
    }

    [Fact]
    public void AutoExpandOnlyWhenThereAreChangesAndNotTooMany()
    {
        Assert.False(TreeExpansion.ShouldAutoExpand(0));
        Assert.True(TreeExpansion.ShouldAutoExpand(1));
        Assert.True(TreeExpansion.ShouldAutoExpand(TreeExpansion.AutoExpandLimit));
        Assert.False(TreeExpansion.ShouldAutoExpand(TreeExpansion.AutoExpandLimit + 1));
    }
}
