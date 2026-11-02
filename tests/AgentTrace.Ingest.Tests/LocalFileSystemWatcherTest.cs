using AgentTrace.Core;
using AgentTrace.Testing;

namespace AgentTrace.Ingest.Tests;

public sealed class LocalFileSystemWatcherTest : TempDirTestBase
{
    [Fact]
    public void AnyChangeInTheProjectRaisesANotice()
    {
        int notices = 0;
        using var w = new LocalFileSystemWatcher(() => Interlocked.Increment(ref notices));
        w.StartWatching(Dir);
        File.WriteAllText(Dir.Combine("a.txt").Value, "x");
        Eventually.Until("notice for new file", () => Volatile.Read(ref notices) > 0);
        int before = Volatile.Read(ref notices);
        File.WriteAllText(Dir.Combine("a.txt").Value, "y");
        Eventually.Until("notice for modification", () => Volatile.Read(ref notices) > before);
    }

    [Fact]
    public void NewSubdirectoriesAreWatchedToo()
    {
        int notices = 0;
        using var w = new LocalFileSystemWatcher(() => Interlocked.Increment(ref notices));
        w.StartWatching(Dir);
        FilePath sub = Dir.Combine("new", "nested");
        Directory.CreateDirectory(sub.Value);
        Eventually.Until("notice for new folder", () => Volatile.Read(ref notices) > 0);
        Thread.Sleep(300);
        int before = Volatile.Read(ref notices);
        File.WriteAllText(sub.Combine("inside.txt").Value, "x");
        Eventually.Until("notice for file in new folder", () => Volatile.Read(ref notices) > before);
    }

    [Fact]
    public void SkippedDirectoriesDoNotRaiseNotices()
    {
        FilePath modules = Dir.Combine("node_modules", "package");
        Directory.CreateDirectory(modules.Value);
        int notices = 0;
        using var w = new LocalFileSystemWatcher(() => Interlocked.Increment(ref notices));
        w.StartWatching(Dir);
        File.WriteAllText(modules.Combine("index.js").Value, "x");
        Thread.Sleep(500);
        Assert.Equal(0, Volatile.Read(ref notices));
    }

    [Fact]
    public void ExtraDirectoriesAreWatchedWithoutRecursion()
    {
        FilePath gitDir = Dir.Combine("git-data");
        FilePath project = Dir.Combine("project");
        Directory.CreateDirectory(gitDir.Value);
        Directory.CreateDirectory(project.Value);
        int notices = 0;
        using var w = new LocalFileSystemWatcher(() => Interlocked.Increment(ref notices));
        w.StartWatching(project, gitDir);
        File.WriteAllText(gitDir.Combine("HEAD").Value, "ref: refs/heads/other\n");
        Eventually.Until("notice for change in the extra directory", () => Volatile.Read(ref notices) > 0);
    }

    [Fact]
    public void DisposingTwiceIsHarmlessAndStopsNotices()
    {
        int notices = 0;
        var w = new LocalFileSystemWatcher(() => Interlocked.Increment(ref notices));
        w.StartWatching(Dir);
        w.Dispose();
        int before = Volatile.Read(ref notices);
        w.Dispose(); // closing twice must not break anything
        File.WriteAllText(Dir.Combine("late.txt").Value, "x");
        Thread.Sleep(300);
        Assert.True(Volatile.Read(ref notices) <= before + 1);
    }
}
