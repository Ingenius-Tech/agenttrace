using AgentTrace.Core.Areas;

namespace AgentTrace.Core.Tests.Areas;

public sealed class AreaTaskSummaryTest
{
    private static readonly FilePath Root = new(OperatingSystem.IsWindows() ? @"C:\proj" : "/proj");

    private static FilePath F(string name) => Root.Combine(name);

    private static AreaFileActivity Act(string name, int reads = 0, int writes = 0, int external = 0, int? writeOrder = null) =>
        new(F(name), reads, writes, external, 0, 0, writeOrder ?? (writes + external > 0 ? 0 : null));

    private static AreaActivity Activity(params (int Turn, AreaFileActivity[] Files)[] turns) =>
        new(turns.Select(t => new AreaTurnActivity(t.Turn, t.Files.ToValueList())).ToValueList());

    private static ValueList<AreaTask> Tasks(params (int First, int Last)[] ranges) =>
        ranges.Select((r, i) => new AreaTask(i, r.First, r.Last, true)).ToValueList();

    /// <summary>Files named "layer.module.kind.cs"; "-" means no area, a "!" at the start of the module marks it shared.</summary>
    private static AreaClassification? Classify(FilePath path)
    {
        string[] parts = path.FileName.Split('.');
        if (parts.Length < 4) return null;
        string? Part(string p) => p == "-" ? null : p.TrimStart('!');
        return new AreaClassification("repo", new AreaAssignment(Part(parts[0]), Part(parts[1]), Part(parts[2])), parts[1].StartsWith('!'));
    }

    private static AreaTaskSummary Summary(AreaActivity activity, bool testKind = true, params (int First, int Last)[] ranges) =>
        AreaTaskSummaries.Of(Tasks(ranges.Length == 0 ? [(0, 5)] : ranges), activity, Classify, testKind)[0];

    private static string[] Ids(ValueList<AreaRef> refs) => [.. refs.Select(r => r.Id)];

    [Fact]
    public void FilesLayersAndModulesAreCountedAndTheLayersComeOutInTheOrderOfTheFirstChange()
    {
        AreaTaskSummary s = Summary(Activity(
            (0, [Act("app.shop.-.cs", reads: 1, writes: 1, writeOrder: 4), Act("data.shop.-.cs", writes: 1, writeOrder: 1), Act("api.orders.-.cs", reads: 2)]),
            (1, [Act("data.shop.-.cs", writes: 2)])));
        Assert.Equal(3, s.Files);
        Assert.Equal(2, s.WrittenFiles);
        Assert.Equal(["data", "app", "api"], Ids(s.Layers)); // changes first, then what was only read
        Assert.Equal(["data", "app"], Ids(s.LayerOrder));
        Assert.Equal(["shop", "orders"], Ids(s.Modules));
        Assert.Equal(["shop"], Ids(s.WrittenModules));
    }

    [Fact]
    public void ATaskOnlyCountsItsOwnTurns()
    {
        AreaActivity activity = Activity((0, [Act("a.m.-.cs", writes: 1)]), (3, [Act("b.m.-.cs", writes: 1)]));
        AreaTaskSummary second = AreaTaskSummaries.Of(Tasks((0, 2), (3, 4)), activity, Classify, true)[1];
        Assert.Equal(1, second.Files);
        Assert.Equal(["b"], Ids(second.LayerOrder));
    }

    [Fact]
    public void ChangingCodeWithoutTouchingTestsIsFlaggedOnlyWhenATestKindExists()
    {
        AreaActivity untested = Activity((0, [Act("-.m.-.cs", writes: 1)]));
        Assert.Equal(AreaBadges.UntestedChange, Summary(untested).Badges);

        // Without a "test" kind in the rules it is not computed: no badge, and the summary says so
        AreaTaskSummary unknown = Summary(untested, testKind: false);
        Assert.Equal(AreaBadges.None, unknown.Badges);
        Assert.False(unknown.TestsKnown);
    }

    [Fact]
    public void ReadingOrChangingATestFileClearsTheUntestedBadge()
    {
        Assert.Equal(AreaBadges.None, Summary(Activity((0, [Act("-.m.-.cs", writes: 1), Act("-.m.test.cs", reads: 1)]))).Badges);
        Assert.Equal(AreaBadges.None, Summary(Activity((0, [Act("-.m.-.cs", writes: 1)]), (1, [Act("-.m.test.cs", writes: 1)]))).Badges);
    }

    [Fact]
    public void OnlyChangingTestsIsNotAnUntestedChangeAndReadsAloneNeverAre()
    {
        Assert.Equal(AreaBadges.None, Summary(Activity((0, [Act("-.m.test.cs", writes: 1)]))).Badges);
        Assert.Equal(AreaBadges.None, Summary(Activity((0, [Act("-.m.-.cs", reads: 3)]))).Badges);
    }

    [Fact]
    public void ChangingTwoModulesIsCrossModule()
    {
        AreaBadges badges = Summary(Activity((0, [Act("-.a.test.cs", writes: 1), Act("-.b.test.cs", external: 1)]))).Badges;
        Assert.Equal(AreaBadges.CrossModule, badges);
        Assert.Equal(AreaBadges.None, Summary(Activity((0, [Act("-.a.test.cs", writes: 1), Act("-.b.test.cs", reads: 1)]))).Badges); // a read does not count
    }

    [Fact]
    public void ChangingAContractOrASharedModuleIsFlagged()
    {
        Assert.Equal(AreaBadges.SharedTouched, Summary(Activity((0, [Act("-.m.contract.cs", writes: 1), Act("-.m.test.cs", reads: 1)]))).Badges);
        Assert.Equal(AreaBadges.SharedTouched, Summary(Activity((0, [Act("-.!core.test.cs", writes: 1)]))).Badges);
        Assert.Equal(AreaBadges.None, Summary(Activity((0, [Act("-.!core.test.cs", reads: 1)]))).Badges);
    }

    [Fact]
    public void FilesInNoRepositoryCountAsFilesButHaveNoAreas()
    {
        AreaTaskSummary s = Summary(Activity((0, [Act("outside.cs", writes: 1)])));
        Assert.Equal(1, s.Files);
        Assert.Equal(1, s.WrittenFiles);
        Assert.Empty(s.Layers);
        Assert.Equal(AreaBadges.None, s.Badges);
    }
}
