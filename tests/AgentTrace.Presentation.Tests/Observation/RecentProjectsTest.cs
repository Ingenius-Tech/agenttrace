using AgentTrace.Core;
using AgentTrace.Presentation.Observation;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Observation;

public sealed class RecentProjectsTest : TempDirTestBase
{
    private FilePath DirOf(string name)
    {
        FilePath p = Dir.Combine(name);
        Directory.CreateDirectory(p.Value);
        return p;
    }

    [Fact]
    public void StartsEmptyAndRemembersTheLastOpenedFirst()
    {
        var recent = new RecentProjects(Dir.Combine("hist", "recent.txt"));
        Assert.Empty(recent.List());
        Assert.Null(recent.Last());
        recent.Add(DirOf("a"));
        recent.Add(DirOf("b"));
        Assert.Equal([DirOf("b"), DirOf("a")], recent.List());
        Assert.Equal(DirOf("b"), recent.Last());
    }

    [Fact]
    public void OpeningAgainMovesItToTheTopWithoutDuplicates()
    {
        var recent = new RecentProjects(Dir.Combine("recent.txt"));
        recent.Add(DirOf("a"));
        recent.Add(DirOf("b"));
        recent.Add(DirOf("a"));
        Assert.Equal([DirOf("a"), DirOf("b")], recent.List());
    }

    [Fact]
    public void KeepsOnlyTheLastEightAndSurvivesANewInstance()
    {
        FilePath file = Dir.Combine("recent.txt");
        var recent = new RecentProjects(file);
        for (int i = 0; i < 12; i++) recent.Add(DirOf("p" + i));
        IReadOnlyList<FilePath> list = new RecentProjects(file).List();
        Assert.Equal(RecentProjects.Max, list.Count);
        Assert.Equal(DirOf("p11"), list[0]);
        Assert.Equal(DirOf("p4"), list[7]);
    }

    [Fact]
    public void FoldersThatNoLongerExistAreDropped()
    {
        var recent = new RecentProjects(Dir.Combine("recent.txt"));
        FilePath gone = DirOf("gone");
        recent.Add(DirOf("kept"));
        recent.Add(gone);
        Directory.Delete(gone.Value);
        Assert.Equal([DirOf("kept")], recent.List());
        Assert.Equal(DirOf("kept"), recent.Last());
    }

    [Fact]
    public void GarbageInTheFileIsIgnoredAndAnUnwritableLocationDoesNotThrow()
    {
        FilePath file = Dir.Combine("recent.txt");
        File.WriteAllText(file.Value, "\n   \n" + DirOf("ok") + "\n\0odd\n");
        Assert.Equal([DirOf("ok")], new RecentProjects(file).List());
        FilePath blocker = Dir.Combine("blocker");
        File.WriteAllText(blocker.Value, "x");
        new RecentProjects(blocker.Combine("inside", "recent.txt")).Add(DirOf("z")); // the parent is a file: must not fail
    }
}
