using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Presentation.Areas;
using AgentTrace.Presentation.Heatmap;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Areas;

public sealed class AreaTextTest
{
    private static AreaRef R(string id) => new("repo", id);

    private static AreaTaskSummary Summary(int files, string[] layers, string[] modules, string[] order, AreaBadges badges = AreaBadges.None, bool testsKnown = true,
        string[]? writtenModules = null) =>
        new(0, files, files, layers.Select(R).ToValueList(), modules.Select(R).ToValueList(), order.Select(R).ToValueList(),
            (writtenModules ?? []).Select(R).ToValueList(), badges, testsKnown);

    private static string Name(AreaRef r) => char.ToUpperInvariant(r.Id[0]) + r.Id[1..];

    [Fact]
    public void TheCountsOfATaskUseSingularAndPluralForms()
    {
        Assert.Equal("3 files · 2 layers · 1 module", AreaText.TaskCounts(Summary(3, ["a", "b"], ["m"], [])));
        Assert.Equal("1 file · 1 layer · 2 modules", AreaText.TaskCounts(Summary(1, ["a"], ["m", "n"], [])));
        Assert.Equal("1,200 files · 0 layers · 0 modules", AreaText.TaskCounts(Summary(1200, [], [], [])));
    }

    [Fact]
    public void TheOrderOfTheChangesListsTheLayersWithArrowsOrIsAbsent()
    {
        Assert.Equal("First changes: Data → Domain → Api", AreaText.TaskOrder(Summary(3, [], [], ["data", "domain", "api"]), Name));
        Assert.Null(AreaText.TaskOrder(Summary(1, ["data"], [], []), Name));
    }

    [Fact]
    public void EachBadgeHasItsText()
    {
        AreaTaskSummary all = Summary(3, [], [], [], AreaBadges.UntestedChange | AreaBadges.CrossModule | AreaBadges.SharedTouched, writtenModules: ["a", "b", "c"]);
        Assert.Equal(["Code changed but no tests were touched", "Changes in 3 modules", "Shared or contract code changed"], AreaText.BadgeTexts(all));
        Assert.Empty(AreaText.BadgeTexts(Summary(3, [], [], [])));
    }

    [Fact]
    public void WithoutATestKindTheUntestedBadgeIsNotComputedAndSaysSo()
    {
        IReadOnlyList<string> texts = AreaText.BadgeTexts(Summary(3, [], [], [], testsKnown: false));
        Assert.Equal(["Tests are not checked: no area of kind \"test\" is defined"], texts);
    }

    [Fact]
    public void TheSnapshotKnowsWhetherTheRulesDefineATestKindAndNamesTheAreas()
    {
        FilePath root = new(OperatingSystem.IsWindows() ? @"C:\proj" : "/proj");
        AreaRule Rule(string id, string name, AreaDimension d) => new(id, name, d, ["**"], ValueList<string>.Empty);
        AreaRepoSet Repos(params AreaRule[] rules)
        {
            var set = new AreaRules(rules.ToValueList());
            return new AreaRepoSet([new AreaRepo(root, "proj", set, new AreaClassifier(root, set), AreaSource.File, ValueList<AreaRuleError>.Empty)]);
        }
        var flow = new AgentTrace.Core.Flow.FlowBuilder().Snapshot();
        var withTests = new AreasSnapshot(flow, AreaActivity.Empty, Repos(Rule("test", "Tests", AreaDimension.Kind)), root);
        var without = new AreasSnapshot(flow, AreaActivity.Empty, Repos(Rule("docs", "Docs", AreaDimension.Kind), Rule("api", "API", AreaDimension.Layer)), root);
        Assert.Equal("API", without.NameOf(new AreaRef(root.Value, "api")));
        Assert.Equal("gone", without.NameOf(new AreaRef(root.Value, "gone")));
        Assert.Empty(withTests.Summaries); // no tasks without turns
        Assert.Empty(AreaText.BadgeTexts(new AreaTaskSummary(0, 0, 0, [], [], [], [], AreaBadges.None, true)));
    }

    [Fact]
    public void TheTextsAreSpanishUnderEs()
    {
        using var _ = CultureScope.Spanish();
        Assert.Equal("3 archivos · 1 capa · 2 módulos", AreaText.TaskCounts(Summary(3, ["a"], ["m", "n"], []))); // lang-ok
        Assert.Equal("Primeros cambios: Data → Api", AreaText.TaskOrder(Summary(1, [], [], ["data", "api"]), Name)); // lang-ok
        Assert.Equal("Tarea 2", AreaText.TaskTitle(new AreaTask(1, 1, 1, true))); // lang-ok
        Assert.Equal("Tarea 2 (sin prompt)", AreaText.TaskTitle(new AreaTask(1, 1, 1, false))); // lang-ok
        Assert.Equal("Capa", AreaText.Group(AreaDimension.Layer)); // lang-ok
        Assert.Equal("Cambios en 2 módulos", AreaText.BadgeTexts(Summary(2, [], [], [], AreaBadges.CrossModule, writtenModules: ["a", "b"]))[0]); // lang-ok
    }

    [Fact]
    public void TheCountsOfACellListOnlyTheActiveKinds()
    {
        var a = new FileActivity(3, 0, 1, 0, 0, 2);
        Assert.Equal("3 reads · 1 write · 2 errors", AreaText.Counts(a, ActivityFilter.All));
        Assert.Equal("1 write", AreaText.Counts(a, new ActivityFilter(false, true, false)));
        Assert.Equal("+1 more file", AreaText.MoreFiles(1));
        Assert.Equal("Task 3", AreaText.TaskTitle(new AreaTask(2, 5, 6, true)));
        Assert.Equal("Turn 1", AreaText.TurnTitle(0));
    }
}
