using AgentTrace.Core.Areas;
using AgentTrace.Testing;

namespace AgentTrace.Core.Tests.Areas;

public sealed class GlobPatternTest
{
    private static bool Matches(string pattern, string path) => GlobPattern.TryCreate(pattern, out _)!.IsMatch(path);

    /// <summary>The contract tables of <c>docs/AREAS.md</c>: every row is run against the implementation.</summary>
    private static (List<(string Pattern, string Path, bool Expected)> Matches, List<(string Pattern, GlobError Error)> Rejections) DocTables()
    {
        string doc = File.ReadAllText(TestPaths.RepoRoot().Combine("docs", "AREAS.md").Value);
        List<(string, string, bool)> matches = [];
        List<(string, GlobError)> rejections = [];
        foreach (string line in doc.Split('\n'))
        {
            string[] cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (!line.TrimStart().StartsWith('|') || cells.Any(c => c.StartsWith("---", StringComparison.Ordinal))) continue;
            static string Unquote(string cell) => cell == "(empty string)" ? "" : cell.Trim('`');
            if (cells.Length == 3 && cells[2] is "yes" or "no") matches.Add((Unquote(cells[0]), Unquote(cells[1]), cells[2] == "yes"));
            else if (cells.Length == 2 && Enum.TryParse(cells[1], out GlobError error)) rejections.Add((Unquote(cells[0]), error));
        }
        return (matches, rejections);
    }

    [Fact]
    public void EveryRowOfTheDocumentedMatchTableHoldsInTheImplementation()
    {
        var (matches, _) = DocTables();
        Assert.True(matches.Count >= 30, "the table was not parsed");
        foreach (var (pattern, path, expected) in matches)
        {
            Assert.True(expected == Matches(pattern, path), $"'{pattern}' on '{path}' should be {(expected ? "a match" : "no match")}");
        }
    }

    [Fact]
    public void EveryRowOfTheDocumentedRejectionTableHoldsInTheImplementation()
    {
        var (_, rejections) = DocTables();
        Assert.True(rejections.Count >= 10, "the table was not parsed");
        foreach (var (pattern, error) in rejections)
        {
            Assert.Equal(error, GlobPattern.Validate(pattern));
            Assert.Null(GlobPattern.TryCreate(pattern, out GlobError? reported));
            Assert.Equal(error, reported);
        }
    }

    [Fact]
    public void TheDocumentedRejectionsCoverEveryError()
    {
        var (_, rejections) = DocTables();
        Assert.Equal(Enum.GetValues<GlobError>().OrderBy(e => e), rejections.Select(r => r.Error).Distinct().OrderBy(e => e));
    }

    [Theory]
    [InlineData("a.b", "a.b", true)]
    [InlineData("a+b", "a+b", true)]
    [InlineData("a+b", "aab", false)]
    [InlineData("(x)", "(x)", true)]
    [InlineData("a|b", "a", false)]
    [InlineData("src/a b.cs", "src/a b.cs", true)]
    public void RegexCharactersAreLiteral(string pattern, string path, bool expected)
    {
        Assert.Equal(expected, Matches(pattern, path));
    }

    [Fact]
    public void MatchingIsCaseSensitive()
    {
        Assert.False(Matches("README.md", "readme.md"));
        Assert.True(Matches("README.md", "README.md"));
    }

    [Fact]
    public void AValidPatternKeepsItsSource()
    {
        Assert.Equal("src/**", GlobPattern.TryCreate("src/**", out GlobError? error)!.Source);
        Assert.Null(error);
    }
}
