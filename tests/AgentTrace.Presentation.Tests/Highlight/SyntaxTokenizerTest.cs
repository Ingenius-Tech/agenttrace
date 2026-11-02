using AgentTrace.Presentation.Highlight;

namespace AgentTrace.Presentation.Tests.Highlight;

public sealed class SyntaxTokenizerTest
{
    /// <summary>"text:TYPE" of each fragment, to read the tests at a glance (the type in uppercase, as in Java).</summary>
    private static List<string> Show(SyntaxTokenizer tokenizer, IReadOnlyList<string> lines, int index)
    {
        var output = new List<string>();
        foreach (Token t in tokenizer.Tokens(index)) output.Add(lines[index][t.Start..t.End] + ":" + t.Type.ToString().ToUpperInvariant());
        return output;
    }

    private static List<string> Show(string fileName, string line)
    {
        string[] lines = [line];
        return Show(SyntaxTokenizer.ForFile(fileName, lines), lines, 0);
    }

    private static List<string> OnlyEnding(List<string> items, string suffix) => items.Where(t => t.EndsWith(suffix, StringComparison.Ordinal)).ToList();

    [Fact]
    public void JavaKeywordsStringsCommentsAndNumbers()
    {
        Assert.Equal<string>(["public:KEYWORD", "class:KEYWORD", "int:KEYWORD", "42:NUMBER", "\"hello\":STRING", "// end:COMMENT"],
            Show("A.java", "public class Foo { int x = 42; String s = \"hello\"; // end"));
    }

    [Fact]
    public void AWordInsideAStringOrCommentIsNotAKeyword()
    {
        Assert.Equal<string>(["\"public static\":STRING"], Show("A.java", "\"public static\""));
        Assert.Equal<string>(["// public class:COMMENT"], Show("A.java", "// public class"));
    }

    [Fact]
    public void IdentifiersContainingAKeywordAreNotKeywords()
    {
        Assert.Empty(Show("A.java", "publicity classy newer"));
        Assert.Equal<string>(["new:KEYWORD"], OnlyEnding(Show("A.java", "foo(new)"), "KEYWORD"));
    }

    [Fact]
    public void NumbersInsideIdentifiersAreNotNumbers()
    {
        Assert.Empty(Show("A.java", "valor1 x2y"));
        Assert.Equal<string>(["3.14:NUMBER", "0xFF:NUMBER"], Show("A.java", "a = 3.14 + 0xFF"));
    }

    [Fact]
    public void StringsHandleEscapedQuotesAndUnterminatedOnesRunToTheEndOfTheLine()
    {
        Assert.Equal<string>(["\"a\\\"b\":STRING"], Show("A.java", "\"a\\\"b\""));
        Assert.Equal<string>(["\"unclosed:STRING"], Show("A.java", "\"unclosed"));
    }

    [Fact]
    public void ALineCommentMarkerInsideAStringDoesNotStartAComment()
    {
        Assert.Equal<string>(["\"http://x\":STRING", "// real:COMMENT"], Show("A.java", "\"http://x\" // real"));
    }

    [Fact]
    public void BlockCommentsSpanSeveralLines()
    {
        string[] lines = ["int a; /* empieza", "sigue public", "termina */ int b;", "public"];
        SyntaxTokenizer t = SyntaxTokenizer.ForFile("A.java", lines);
        Assert.Equal<string>(["/* empieza:COMMENT"], OnlyEnding(Show(t, lines, 0), "COMMENT"));
        Assert.Equal<string>(["sigue public:COMMENT"], Show(t, lines, 1));
        Assert.Equal<string>(["termina */:COMMENT"], OnlyEnding(Show(t, lines, 2), "COMMENT"));
        Assert.True(Show(t, lines, 3).SequenceEqual(["public:KEYWORD"]), "after closing the block, the code goes back to normal");
    }

    [Fact]
    public void ABlockClosedOnTheSameLineDoesNotLeakIntoTheNext()
    {
        string[] lines = ["a /* x */ b", "public"];
        SyntaxTokenizer t = SyntaxTokenizer.ForFile("A.java", lines);
        Assert.Equal<string>(["public:KEYWORD"], Show(t, lines, 1));
    }

    [Fact]
    public void PythonUsesHashCommentsAndItsOwnKeywords()
    {
        Assert.Equal<string>(["def:KEYWORD", "return:KEYWORD", "# nota:COMMENT"],
            Show("a.py", "def f(): return 1 # nota").Where(t => !t.EndsWith("NUMBER", StringComparison.Ordinal)).ToList());
        Assert.Empty(Show("a.py", "public static"));
    }

    [Fact]
    public void JsonKeysAreStringsAndLiteralsAreKeywords()
    {
        Assert.Equal<string>(["\"clave\":STRING", "true:KEYWORD", "12:NUMBER"],
            Show("a.json", "{\"clave\": true, \"n\": 12}").Where(t => !t.StartsWith("\"n\"", StringComparison.Ordinal)).ToList());
    }

    [Fact]
    public void XmlHighlightsTagsAttributeValuesAndComments()
    {
        Assert.Equal<string>(["<dependency:KEYWORD", "\"1.0\":STRING"], Show("pom.xml", "<dependency version=\"1.0\">"));
        Assert.Equal<string>(["</dependency:KEYWORD"], Show("pom.xml", "</dependency>"));
        Assert.Equal<string>(["<!-- nota -->:COMMENT"], Show("a.html", "<!-- nota -->"));
        Assert.Empty(Show("a.xml", "a < b"));
    }

    [Fact]
    public void SqlKeywordsIgnoreCaseAndUseDoubleDashComments()
    {
        Assert.Equal<string>(["SELECT:KEYWORD", "FROM:KEYWORD", "where:KEYWORD", "-- nota:COMMENT"],
            Show("q.sql", "SELECT a FROM t where x -- nota"));
    }

    [Fact]
    public void MarkdownHeadingsAndFencedCodeSpanLines()
    {
        string[] lines = ["# Titulo", "texto normal", "```java", "public class A {}", "```", "despues", "usa `codigo` aqui"];
        SyntaxTokenizer t = SyntaxTokenizer.ForFile("README.md", lines);
        Assert.Equal<string>(["# Titulo:KEYWORD"], Show(t, lines, 0));
        Assert.Empty(Show(t, lines, 1));
        Assert.Equal<string>(["```java:STRING"], Show(t, lines, 2));
        Assert.True(Show(t, lines, 3).SequenceEqual(["public class A {}:STRING"]), "inside a code block it is not highlighted as Java");
        Assert.Equal<string>(["```:STRING"], Show(t, lines, 4));
        Assert.Empty(Show(t, lines, 5));
        Assert.Equal<string>(["`codigo`:STRING"], Show(t, lines, 6));
        string[] noSpace = ["#no space"];
        Assert.Empty(Show(SyntaxTokenizer.ForFile("a.md", noSpace), noSpace, 0));
    }

    [Fact]
    public void PowerShellHasBlockCommentsAndIgnoresCase()
    {
        string[] lines = ["<# ayuda", "mas #>", "Function Get-Algo { Return 1 }"];
        SyntaxTokenizer t = SyntaxTokenizer.ForFile("s.ps1", lines);
        Assert.Equal<string>(["<# ayuda:COMMENT"], Show(t, lines, 0));
        Assert.Equal<string>(["mas #>:COMMENT"], Show(t, lines, 1));
        Assert.Equal<string>(["Function:KEYWORD", "Return:KEYWORD"], Show(t, lines, 2).Where(s => !s.EndsWith("NUMBER", StringComparison.Ordinal)).ToList());
    }

    [Fact]
    public void UnknownExtensionsAndFilesWithoutExtensionArePlain()
    {
        Assert.True(SyntaxTokenizer.ForFile("datos.xyz", ["public class"]).Language.IsPlain);
        Assert.Empty(Show("LICENSE", "public class \"x\""));
        Assert.Equal<string>(["# nota:COMMENT"], Show("Dockerfile", "# nota"));
    }

    [Fact]
    public void TooManyLinesOrCharactersAreNotHighlighted()
    {
        var many = new List<string>();
        for (int i = 0; i <= SyntaxTokenizer.MaxLines; i++) many.Add("public");
        Assert.True(SyntaxTokenizer.ForFile("A.java", many).Language.IsPlain);
        string[] wide = [new string('x', (int)SyntaxTokenizer.MaxChars + 1)];
        Assert.True(SyntaxTokenizer.ForFile("A.java", wide).Language.IsPlain);
    }

    [Fact]
    public void TokensAreSortedNeverOverlapAndStayInsideTheLine()
    {
        string[] lines = ["public /* a */ \"x\" // y", "\"abc\\", "a = 1 /* b", "*/ if (z) { return 'q'; }", "", "<a href=\"x\">"];
        foreach (string file in new[] { "A.java", "a.py", "a.xml", "a.sql", "a.md" })
        {
            SyntaxTokenizer t = SyntaxTokenizer.ForFile(file, lines);
            for (int i = 0; i < lines.Length; i++)
            {
                int previousEnd = 0;
                foreach (Token token in t.Tokens(i))
                {
                    Assert.True(token.Start >= previousEnd, $"{file} line {i}: overlaps {token}");
                    Assert.True(token.End > token.Start && token.End <= lines[i].Length, $"{file} line {i}: {token}");
                    previousEnd = token.End;
                }
            }
        }
    }

    [Fact]
    public void AsksForTokensOutsideTheFileAndGetsNothing()
    {
        SyntaxTokenizer t = SyntaxTokenizer.ForFile("A.java", ["public"]);
        Assert.Empty(t.Tokens(-1));
        Assert.Empty(t.Tokens(5));
    }
}
