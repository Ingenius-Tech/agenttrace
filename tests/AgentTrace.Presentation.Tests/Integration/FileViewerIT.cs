using System.Diagnostics;
using AgentTrace.Core;
using AgentTrace.Ingest;
using AgentTrace.Presentation.Highlight;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Integration;

/// <summary>
/// e2e of the viewer without a screen: an external process rewrites a file again and again while it is read with the same reader
/// the application uses, and a highlighting pass over a real file of the project.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FileViewerIT : TempDirTestBase
{
    [Fact]
    public void ReadingNeverFailsWhileAnotherProcessKeepsRewritingTheFile()
    {
        FilePath target = Dir.Combine("live.txt");
        File.WriteAllText(target.Value, "version 0\nlínea con ñ\n"); // lang-ok: encoding data
        using Process process = ExternalProcess.StartRewriter(target.Value, 150, 3);

        int reads = 0;
        int readable = 0;
        try
        {
            while (!process.HasExited)
            {
                FileContent content = FileContentReader.Read(target);
                reads++;
                Assert.NotNull(content);
                Assert.True(content.Readable || content.Problem is not null, "either there is text or there is a reason: " + content);
                if (content.Readable && content.Lines.Count > 0)
                {
                    readable++;
                    // A read may catch the file halfway, but it must never mix or corrupt the text
                    Assert.True(content.Lines[0].StartsWith("version ", StringComparison.Ordinal), "unexpected first line: " + content.Lines);
                    Assert.Equal("UTF-8", content.Encoding); // the ñ must not make it be taken for another encoding // lang-ok
                }
            }
        }
        finally
        {
            if (!process.HasExited) process.Kill(true);
        }
        Assert.True(process.WaitForExit(30_000));
        Assert.True(reads > 5, "several reads were expected, there were " + reads);
        Assert.True(readable > 0, "no read returned text");
        FileContent last = FileContentReader.Read(target);
        Assert.Equal(["version 150", "línea con ñ"], last.Lines); // the final read reflects the last version // lang-ok
    }

    [Fact]
    public void HighlightingARealSourceFileFromThisProjectProducesSaneTokens()
    {
        FilePath source = TestPaths.RepoRoot().Combine("src", "AgentTrace.Presentation", "Observation", "ProjectObserver.cs");
        FileContent content = FileContentReader.Read(source);
        Assert.True(content.Readable, "could not read " + source + ": " + content.Problem);
        var tokenizer = SyntaxTokenizer.ForFile(source.FileName, content.Lines);
        Assert.False(tokenizer.Language.IsPlain);
        int keywords = 0;
        int strings = 0;
        int comments = 0;
        List<string> problems = [];
        for (int i = 0; i < content.Lines.Count; i++)
        {
            int previousEnd = 0;
            foreach (Token token in tokenizer.Tokens(i))
            {
                if (token.Start < previousEnd || token.End > content.Lines[i].Length || token.End <= token.Start)
                {
                    problems.Add("line " + (i + 1) + ": " + token);
                }
                previousEnd = token.End;
                switch (token.Type)
                {
                    case TokenType.Keyword: keywords++; break;
                    case TokenType.String: strings++; break;
                    case TokenType.Comment: comments++; break;
                }
            }
        }
        Assert.True(problems.Count == 0, "wrong fragments: " + string.Join("; ", problems));
        Assert.True(keywords > 50 && strings > 0 && comments > 10,
            "a real C# file must have keywords, strings and comments: " + keywords + "/" + strings + "/" + comments);
        Console.WriteLine("[REAL-VIEW] " + source.FileName + ": " + content.Lines.Count + " lines, " + keywords
            + " keywords, " + strings + " strings, " + comments + " comments; " + content.Encoding + ", " + content.LineEnding);
    }
}
