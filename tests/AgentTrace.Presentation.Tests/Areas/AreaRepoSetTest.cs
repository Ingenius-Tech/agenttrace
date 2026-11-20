using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Ingest.Areas;
using AgentTrace.Presentation.Areas;
using AgentTrace.Presentation.Tree;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Areas;

public sealed class AreaRepoSetTest : TempDirTestBase
{
    private const string Valid = "{ \"version\": 1, \"areas\": [ { \"id\": \"a\", \"name\": \"A\", \"dimension\": \"layer\", \"include\": [\"src/**\"] } ] }";

    private static readonly ProjectListing NoFiles = new(ValueList<ListedFile>.Empty, false);

    private void WriteRules(string json)
    {
        FilePath file = AreaRulesReader.FileOf(Dir);
        Directory.CreateDirectory(file.Parent!.Value.Value);
        File.WriteAllText(file.Value, json);
    }

    [Fact]
    public void TheSourceFollowsTheRulesFileFromSuggestedToFileToInvalid()
    {
        AreaRepoSet none = AreaRepoSet.Load([Dir], NoFiles);
        Assert.Equal(AreaSource.Suggested, none.Repos.Single().Source);
        Assert.True(none.NeedsRules);

        WriteRules(Valid);
        AreaRepoSet file = AreaRepoSet.Load([Dir], NoFiles, none);
        Assert.Equal(AreaSource.File, file.Repos.Single().Source);
        Assert.False(file.NeedsRules);
        Assert.Equal(AreaDimension.Layer, file.DefaultDimension);

        WriteRules("{ \"version\": 1, \"areas\": [ ");
        AreaRepoSet invalid = AreaRepoSet.Load([Dir], NoFiles, file);
        AreaRepo repo = invalid.Repos.Single();
        Assert.Equal(AreaSource.Invalid, repo.Source);
        Assert.NotEmpty(repo.Errors);
        Assert.True(invalid.NeedsRules);
        // Never applied partially: the suggested rules classify, the broken file does not
        Assert.DoesNotContain(repo.Rules.Areas, a => a.Id == "a");
    }

    [Fact]
    public void AValidFileWhoseSignatureDidNotChangeIsNotReadAgainButAChangedOneIs()
    {
        WriteRules(Valid);
        AreaRepoSet first = AreaRepoSet.Load([Dir], NoFiles);
        AreaRepoSet same = AreaRepoSet.Load([Dir], NoFiles, first);
        Assert.Same(first.Repos[0], same.Repos[0]);

        WriteRules(Valid.Replace("\"A\"", "\"Changed\"", StringComparison.Ordinal));
        AreaRepoSet changed = AreaRepoSet.Load([Dir], NoFiles, first);
        Assert.NotSame(first.Repos[0], changed.Repos[0]);
        Assert.Equal("Changed", changed.Repos[0].Rules.Areas.Single(a => a.Id == "a").Name);

        File.Delete(AreaRulesReader.FileOf(Dir).Value);
        Assert.Equal(AreaSource.Suggested, AreaRepoSet.Load([Dir], NoFiles, changed).Repos.Single().Source);
    }
}
