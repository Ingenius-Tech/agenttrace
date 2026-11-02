namespace AgentTrace.Core.Tests;

public sealed class FilePathTest
{
    private static readonly string Root = Path.GetFullPath("project");

    private static FilePath P(string relative) => new(Path.Combine(Root, relative));

    [Fact]
    public void NormalizesRelativePathsToAbsoluteAndCollapsesDotSegments()
    {
        Assert.Equal(Path.Combine(Root, "b.txt"), new FilePath(Path.Combine(Root, "a", "..", "b.txt")).Value);
        Assert.Equal(Path.GetFullPath("x.txt"), new FilePath("x.txt").Value);
        Assert.True(Path.IsPathRooted(new FilePath("x.txt").Value));
    }

    [Fact]
    public void IgnoresTheTrailingSeparatorButKeepsTheRoot()
    {
        Assert.Equal(new FilePath(Root), new FilePath(Root + Path.DirectorySeparatorChar));
        string fsRoot = Path.GetPathRoot(Root)!;
        Assert.Equal(fsRoot, new FilePath(fsRoot).Value);
    }

    [Fact]
    public void EqualityAndHashIgnoreCaseOnlyWhereTheFileSystemDoes()
    {
        FilePath lower = P("folder/file.txt");
        FilePath upper = new(lower.Value.ToUpperInvariant());

        Assert.Equal(OperatingSystem.IsWindows(), lower == upper);
        Assert.Equal(OperatingSystem.IsWindows(), lower.Equals(upper));
        if (OperatingSystem.IsWindows()) Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
        Assert.True(lower == P("folder/file.txt"));
        Assert.True(lower != P("folder/other.txt"));
    }

    [Fact]
    public void WorksAsDictionaryAndSetKeyWithTheSameRules()
    {
        var set = new HashSet<FilePath> { P("a.txt"), P("a.txt"), P("b/../a.txt") };

        Assert.Single(set);
        Assert.Equal(OperatingSystem.IsWindows(), set.Contains(new FilePath(P("a.txt").Value.ToUpperInvariant())));
        var map = new Dictionary<FilePath, int> { [P("a.txt")] = 1 };
        Assert.Equal(1, map[P("x/../a.txt")]);
    }

    [Fact]
    public void IsUnderComparesByComponentsNotByPrefix()
    {
        Assert.True(P("src/main/A.java").IsUnder(P("src")));
        Assert.True(P("src/main").IsUnder(new FilePath(Root)));
        Assert.False(P("srcmain/A.java").IsUnder(P("src")));
        Assert.False(P("src").IsUnder(P("src/main")));
        Assert.False(P("other/x").IsUnder(P("src")));
    }

    [Fact]
    public void IsUnderIsTrueForTheSamePathLikeStartsWithInJava()
    {
        Assert.True(P("src").IsUnder(P("src")));
    }

    [Fact]
    public void IsUnderWorksWithTheFileSystemRoot()
    {
        var fsRoot = new FilePath(Path.GetPathRoot(Root)!);

        Assert.True(new FilePath(Root).IsUnder(fsRoot));
        Assert.True(fsRoot.IsUnder(fsRoot));
        Assert.False(fsRoot.IsUnder(new FilePath(Root)));
    }

    [Fact]
    public void IsUnderIgnoresCaseOnlyOnWindows()
    {
        FilePath upperRoot = new(P("Folder").Value.ToUpperInvariant());

        Assert.Equal(OperatingSystem.IsWindows(), P("folder/a.txt").IsUnder(upperRoot));
    }

    [Fact]
    public void FileNameAndParent()
    {
        FilePath file = P("src/main/A.java");

        Assert.Equal("A.java", file.FileName);
        Assert.Equal(P("src/main"), file.Parent);
        Assert.Equal(P("src"), file.Parent!.Value.Parent);
    }

    [Fact]
    public void TheRootHasNoParentAndNoFileName()
    {
        var fsRoot = new FilePath(Path.GetPathRoot(Root)!);

        Assert.Null(fsRoot.Parent);
        Assert.Equal("", fsRoot.FileName);
    }

    [Fact]
    public void RelativeToGivesTheRelativePathWithSystemSeparators()
    {
        Assert.Equal(Path.Combine("src", "main", "A.java"), P("src/main/A.java").RelativeTo(new FilePath(Root)));
        Assert.Equal(".", new FilePath(Root).RelativeTo(new FilePath(Root)));
    }

    [Fact]
    public void CombineResolvesRelativePartsAndAbsoluteOnesReplace()
    {
        Assert.Equal(P("src/A.java"), new FilePath(Root).Combine("src").Combine("A.java"));
        Assert.Equal(P("src/A.java"), new FilePath(Root).Combine("src", "A.java"));
        Assert.Equal(P("a.txt"), new FilePath(Root).Combine("x", "..", "a.txt"));
        Assert.Equal(P("z.txt"), P("src").Combine(P("z.txt").Value));
    }

    [Fact]
    public void CompareToOrdersByPathAndIgnoresCaseOnWindows()
    {
        var sorted = new[] { P("z.txt"), P("a.txt"), P("b/c.txt"), P("b.txt") }.Order().ToList();

        Assert.Equal([P("a.txt"), P("b.txt"), P("b/c.txt"), P("z.txt")], sorted);
        Assert.Equal(OperatingSystem.IsWindows(), P("B.txt").CompareTo(P("a.txt")) > 0);
    }

    [Fact]
    public void ToStringIsTheNormalizedPath()
    {
        Assert.Equal(P("a.txt").Value, P("a.txt").ToString());
    }

    [Fact]
    public void ADefaultValueDoesNotThrow()
    {
        FilePath none = default;

        Assert.Equal("", none.Value);
        Assert.Equal(none, default(FilePath));
    }
}
