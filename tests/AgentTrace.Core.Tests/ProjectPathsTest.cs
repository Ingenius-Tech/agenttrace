namespace AgentTrace.Core.Tests;

public sealed class ProjectPathsTest
{
    private static readonly FilePath Root = new(OperatingSystem.IsWindows() ? @"C:\project" : "/project");

    [Theory]
    [InlineData(".gitignore")]
    [InlineData(".claude/settings.json")]
    [InlineData("src/.cache/x.bin")]
    [InlineData("src/.hidden")]
    [InlineData(".github/workflows/ci.yml")]
    public void ADottedSegmentBelowTheRootHidesThePath(string relative)
    {
        Assert.True(ProjectPaths.IsHidden(Root, Root.Combine(relative.Split('/'))));
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("src/Program.cs")]
    [InlineData("docs/a.b.c/notes.md")]
    [InlineData("src/file.with.dots.cs")]
    public void OrdinaryPathsAreNotHidden(string relative)
    {
        Assert.False(ProjectPaths.IsHidden(Root, Root.Combine(relative.Split('/'))));
    }

    [Fact]
    public void OnlyThePartBelowTheRootCounts()
    {
        var dotted = new FilePath(OperatingSystem.IsWindows() ? @"C:\home\.config\project" : "/home/.config/project");
        Assert.False(ProjectPaths.IsHidden(dotted, dotted.Combine("src", "a.cs")));
        Assert.True(ProjectPaths.IsHidden(dotted, dotted.Combine(".git", "config")));
        Assert.False(ProjectPaths.IsHidden(dotted, dotted));
    }

    [Fact]
    public void PathsOutsideTheRootAreNotHiddenByThisRule()
    {
        var other = new FilePath(OperatingSystem.IsWindows() ? @"C:\other\.claude\x" : "/other/.claude/x");
        Assert.False(ProjectPaths.IsHidden(Root, other));
    }
}
