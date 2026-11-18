using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Ingest.Areas;
using AgentTrace.Testing;

namespace AgentTrace.Ingest.Tests.Areas;

public sealed class AreaRulesReaderTest : TempDirTestBase
{
    private void Write(string json)
    {
        FilePath file = AreaRulesReader.FileOf(Dir);
        Directory.CreateDirectory(file.Parent!.Value.Value);
        File.WriteAllText(file.Value, json);
    }

    [Fact]
    public void TheFileLivesInTheAgenttraceFolderOfTheRepository()
    {
        Assert.Equal(Dir.Combine(".agenttrace", "areas.json"), AreaRulesReader.FileOf(Dir));
    }

    [Fact]
    public void AMissingFileIsNotAnError()
    {
        AreaRulesRead read = AreaRulesReader.Read(Dir);
        Assert.False(read.Found);
        Assert.Null(read.Rules);
        Assert.Empty(read.Errors);
    }

    [Fact]
    public void TheSignatureChangesWhenTheFileIsCreatedOrRewrittenAndNotOtherwise()
    {
        Assert.False(AreaRulesReader.SignatureOf(Dir).Exists);
        Write("{ \"version\": 1, \"areas\": [] }");
        AreaRulesSignature first = AreaRulesReader.SignatureOf(Dir);
        Assert.True(first.Exists);
        Assert.Equal(first, AreaRulesReader.SignatureOf(Dir));

        Write("{ \"version\": 1, \"areas\": [ ] }");
        Assert.NotEqual(first, AreaRulesReader.SignatureOf(Dir));
    }

    [Fact]
    public void AValidFileIsReadWithItsAreasInOrder()
    {
        Write("""
            { "version": 1, "extra": 5, "areas": [
              { "id": "data", "name": "Data", "dimension": "layer", "include": ["src/**/Migrations/**"], "exclude": [], "unknown": true },
              { "id": "api", "name": "API", "dimension": "module", "include": ["api/**"], "shared": true }
            ] }
            """);
        AreaRulesRead read = AreaRulesReader.Read(Dir);
        Assert.True(read.Found);
        Assert.Empty(read.Errors);
        Assert.Equal(["data", "api"], read.Rules!.Areas.Select(a => a.Id).ToArray());
        Assert.False(read.Rules.Areas[0].Shared);
        Assert.True(read.Rules.Areas[1].Shared);
        Assert.Equal(["src/**/Migrations/**"], read.Rules.Areas[0].Include);
    }

    [Fact]
    public void InvalidJsonIsReportedWithItsLine()
    {
        Write("{\n  \"version\": 1,\n  \"areas\": [ oops ]\n}");
        AreaRulesRead read = AreaRulesReader.Read(Dir);
        Assert.True(read.Found);
        Assert.Null(read.Rules);
        AreaRuleError e = Assert.Single(read.Errors);
        Assert.Equal(AreaRuleErrorKind.InvalidJson, e.Kind);
        Assert.Equal(3, e.Line);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("")]
    public void ATopLevelThatIsNotAnObjectIsInvalidJson(string json)
    {
        Write(json);
        Assert.Equal(AreaRuleErrorKind.InvalidJson, Assert.Single(AreaRulesReader.Read(Dir).Errors).Kind);
    }

    [Fact]
    public void CommentsAndTrailingCommasAreNotAccepted()
    {
        Write("""{ "version": 1, "areas": [], }""");
        Assert.Equal(AreaRuleErrorKind.InvalidJson, Assert.Single(AreaRulesReader.Read(Dir).Errors).Kind);
        Write("""{ "version": 1, /* x */ "areas": [] }""");
        Assert.Equal(AreaRuleErrorKind.InvalidJson, Assert.Single(AreaRulesReader.Read(Dir).Errors).Kind);
    }

    [Fact]
    public void ValidationErrorsComeFromTheCoreAndDiscardTheWholeFile()
    {
        Write("""
            { "version": 2, "areas": [
              { "id": "a", "name": "A", "dimension": "layer", "include": ["x"] },
              { "id": "a", "name": "B", "dimension": "tier", "include": ["{y}"] }
            ] }
            """);
        AreaRulesRead read = AreaRulesReader.Read(Dir);
        Assert.Null(read.Rules);
        Assert.Equal(
            [AreaRuleErrorKind.UnsupportedVersion, AreaRuleErrorKind.DuplicateId, AreaRuleErrorKind.UnknownDimension, AreaRuleErrorKind.InvalidGlob],
            read.Errors.Select(e => e.Kind).ToArray());
    }

    [Fact]
    public void PropertiesOfTheWrongTypeCountAsMissing()
    {
        Write("""{ "version": "1", "areas": [ { "id": 5, "name": "A", "dimension": "layer", "include": "x" } ] }""");
        AreaRulesRead read = AreaRulesReader.Read(Dir);
        Assert.Null(read.Rules);
        Assert.All(read.Errors, e => Assert.Equal(AreaRuleErrorKind.MissingField, e.Kind));
        Assert.Equal(["version", "id", "include"], read.Errors.Select(e => e.Field).ToArray());
    }

    [Fact]
    public void AFileThatCannotBeReadIsReportedAsUnreadable()
    {
        // Held open without sharing: reading it throws an IO error
        Write("{}");
        using var held = new FileStream(AreaRulesReader.FileOf(Dir).Value, FileMode.Open, FileAccess.Read, FileShare.None);
        AreaRulesRead read = AreaRulesReader.Read(Dir);
        Assert.Null(read.Rules);
        Assert.Equal(AreaRuleErrorKind.Unreadable, Assert.Single(read.Errors).Kind);
    }
}
