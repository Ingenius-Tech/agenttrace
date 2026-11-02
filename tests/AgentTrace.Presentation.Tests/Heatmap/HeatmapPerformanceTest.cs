using System.Diagnostics;
using AgentTrace.Core;
using AgentTrace.Core.Knowledge;
using AgentTrace.Git;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Presentation.Tree;
using AgentTrace.Testing;
using static AgentTrace.Presentation.Tests.Heatmap.HeatFixture;

namespace AgentTrace.Presentation.Tests.Heatmap;

/// <summary>
/// Goals of plan 018: listing + build + layout of 20,000 files in under 200 ms and layout of ~5,000 cells
/// in under 16 ms. Here the limits have a generous margin (the continuous integration machine can be slow); the real times
/// are shown with <c>-showLiveOutput</c> and recorded in the devlog.
/// </summary>
public sealed class HeatmapPerformanceTest : TempDirTestBase
{
    [Fact]
    public void ScanBuildAndLayoutOf20000FilesAreFast()
    {
        for (int d = 0; d < 200; d++)
        {
            string dir = Dir.Combine("pkg" + d / 20, "mod" + d).Value;
            Directory.CreateDirectory(dir);
            for (int f = 0; f < 100; f++) File.WriteAllBytes(Path.Combine(dir, "f" + f + ".txt"), new byte[1 + (f * 37 + d) % 3000]);
        }
        IgnoreRules rules = IgnoreRules.WithoutGit(Dir);
        FileStatusIndex index = FileStatusIndex.Empty(Dir);
        KnowledgeGraph graph = new(
            [.. Enumerable.Range(0, 300).Select(i => new KnowledgeGraph.Node(
                Dir.Combine("pkg" + i % 10, "mod" + i % 200, "f" + i % 100 + ".txt"), null, 1 + i % 7, 0, i % 3, 0, 0, 1, 0, 0, i % 5))
                .DistinctBy(n => n.Path)],
            ValueList<KnowledgeGraph.Edge>.Empty, 1);

        TimeSpan scan = TimeSpan.Zero, build = TimeSpan.Zero, layout = TimeSpan.Zero, best = TimeSpan.MaxValue;
        // The first pass warms up the file system cache and the JIT; the best of three counts
        for (int round = 0; round < 3; round++)
        {
            var sw = Stopwatch.StartNew();
            ProjectListing listing = ProjectScanner.Scan(Dir, rules, index, ProjectScanner.DefaultMaxFiles);
            TimeSpan t1 = sw.Elapsed;
            HeatTree tree = HeatTree.Build(Dir, listing, graph, index);
            TimeSpan t2 = sw.Elapsed;
            TreemapLayout view = TreemapLayout.Of(tree.Root, 1600, 1000, HeatArea.Size, ActivityFilter.All);
            TimeSpan t3 = sw.Elapsed;
            Assert.Equal(20_000, listing.Files.Count);
            Assert.False(listing.Truncated);
            Assert.NotEmpty(view.Cells);
            if (t3 < best)
            {
                best = t3;
                (scan, build, layout) = (t1, t2 - t1, t3 - t2);
            }
        }
        Console.WriteLine($"[performance] 20,000 files: listing {scan.TotalMilliseconds:F0} ms, build {build.TotalMilliseconds:F0} ms, layout {layout.TotalMilliseconds:F0} ms, total {best.TotalMilliseconds:F0} ms (target 200 ms)");
        Assert.True(best < TimeSpan.FromMilliseconds(1500), "demasiado lento: " + best.TotalMilliseconds + " ms");
    }

    [Fact]
    public void LayoutOfAboutFiveThousandCellsIsFast()
    {
        // 50 folders of 100 files: about 5,050 cells in a large view
        ListedFile[] files = [.. Enumerable.Range(0, 5000).Select(i => Listed("d" + i / 100 + "/f" + i % 100 + ".txt", 1000 + (i * 7919) % 50_000))];
        HeatTree tree = Build(Listing(files), Session(1));
        TreemapLayout view = TreemapLayout.Of(tree.Root, 3000, 2000, HeatArea.Size, ActivityFilter.All);
        Assert.InRange(view.Cells.Count, 4500, 5100);

        TimeSpan best = TimeSpan.MaxValue;
        for (int round = 0; round < 10; round++)
        {
            var sw = Stopwatch.StartNew();
            TreemapLayout.Of(tree.Root, 3000, 2000, HeatArea.Size, ActivityFilter.All);
            if (sw.Elapsed < best) best = sw.Elapsed;
        }
        Console.WriteLine($"[performance] layout of {view.Cells.Count} cells: {best.TotalMilliseconds:F1} ms (target 16 ms)");
        Assert.True(best < TimeSpan.FromMilliseconds(100), "demasiado lento: " + best.TotalMilliseconds + " ms");
    }

    /// <summary>Starts from this source file so as not to depend on where it is compiled (ArtifactsPath can be outside the repository).</summary>
    private static FilePath RepoRootFromSource([System.Runtime.CompilerServices.CallerFilePath] string? sourceFile = null)
    {
        DirectoryInfo? dir = new FileInfo(sourceFile ?? "").Directory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        Assert.SkipWhen(dir is null, "The repository cannot be found");
        return new FilePath(dir!.FullName);
    }

    /// <summary>Listing + build + layout of this same repository (with real git) and of the files a fictitious session "touches".</summary>
    [Fact]
    public void ThisRepositoryIsScannedBuiltAndLaidOutFast()
    {
        FilePath repoRoot = RepoRootFromSource();
        GitCli cli = GitCli.ForDirectory(repoRoot);
        Assert.SkipUnless(GitRepository.IsAvailable(cli), "This test needs git installed");
        GitRepository repo = GitRepository.Locate(cli, repoRoot) ?? throw new Xunit.Sdk.XunitException("no repository");
        IgnoreRules rules = IgnoreRules.WithGit(repoRoot, repo.IgnoredPaths(repoRoot));
        FileStatusIndex index = FileStatusIndex.Empty(repoRoot);

        TimeSpan scan = TimeSpan.Zero, build = TimeSpan.Zero, layout = TimeSpan.Zero, best = TimeSpan.MaxValue;
        int files = 0, cells = 0;
        for (int round = 0; round < 3; round++)
        {
            var sw = Stopwatch.StartNew();
            ProjectListing listing = ProjectScanner.Scan(repoRoot, rules, index, ProjectScanner.DefaultMaxFiles);
            TimeSpan t1 = sw.Elapsed;
            // One in six files is marked as touched
            KnowledgeGraph graph = new(
                [.. listing.Files.Where((_, i) => i % 6 == 0).Select((f, i) => new KnowledgeGraph.Node(f.Path, null, 1 + i % 5, 0, i % 3, 0, 0, 1, 0, 0, i % 4))],
                ValueList<KnowledgeGraph.Edge>.Empty, 1);
            HeatTree tree = HeatTree.Build(repoRoot, listing, graph, index);
            TimeSpan t2 = sw.Elapsed;
            TreemapLayout view = TreemapLayout.Of(tree.Root, 1600, 1000, HeatArea.Size, ActivityFilter.All);
            TimeSpan t3 = sw.Elapsed;
            if (t3 < best)
            {
                best = t3;
                (scan, build, layout) = (t1, t2 - t1, t3 - t2);
                (files, cells) = (listing.Files.Count, view.Cells.Count);
            }
        }
        Console.WriteLine($"[performance] this repository ({files} files, {cells} cells): listing {scan.TotalMilliseconds:F0} ms, build {build.TotalMilliseconds:F0} ms, layout {layout.TotalMilliseconds:F0} ms, total {best.TotalMilliseconds:F0} ms (target 200 ms)");
        Assert.True(files > 100);
        Assert.True(best < TimeSpan.FromMilliseconds(1500), "demasiado lento: " + best.TotalMilliseconds + " ms");
    }
}
