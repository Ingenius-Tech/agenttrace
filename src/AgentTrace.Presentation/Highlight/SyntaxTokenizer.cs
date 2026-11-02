namespace AgentTrace.Presentation.Highlight;

/// <summary>
/// Dependency-free per-language highlighting: keywords, strings, comments and numbers according to a <see cref="LanguageSpec"/>.
/// The file is walked once to know which lines start inside a multi-line block (for example a
/// <c>/* ... */</c> comment); after that each line is analyzed on demand. It understands neither multi-line strings
/// nor the grammar: it is presentation, not analysis. What exceeds the limits is shown without highlighting.
/// </summary>
public sealed class SyntaxTokenizer
{
    /// <summary>Above this nothing is highlighted: the prior walk and the painting cost more than they bring.</summary>
    public const int MaxLines = 20_000;

    public const long MaxChars = 1_000_000;

    private readonly IReadOnlyList<string> _lines;
    private readonly bool[] _startsInBlock;

    private SyntaxTokenizer(LanguageSpec spec, IReadOnlyList<string> lines, bool[] startsInBlock)
    {
        Language = spec;
        _lines = lines;
        _startsInBlock = startsInBlock;
    }

    public LanguageSpec Language { get; }

    /// <summary>A tokenizer that marks nothing.</summary>
    public static SyntaxTokenizer Plain(IReadOnlyList<string> lines) => new(LanguageSpec.Plain, lines, new bool[lines.Count]);

    public static SyntaxTokenizer ForFile(string fileName, IReadOnlyList<string> lines)
    {
        LanguageSpec spec = LanguageSpec.ForFile(fileName);
        if (spec.IsPlain || lines.Count > MaxLines || TotalChars(lines) > MaxChars) return Plain(lines);
        var states = new bool[lines.Count];
        bool inBlock = false;
        for (int i = 0; i < lines.Count; i++)
        {
            states[i] = inBlock;
            inBlock = Scan(spec, lines[i], inBlock, null);
        }
        return new SyntaxTokenizer(spec, lines, states);
    }

    /// <summary>
    /// For lines that are not contiguous (those of a diff): each one is analyzed as if it started outside a block, because
    /// what lies between them is not in the list.
    /// </summary>
    public static SyntaxTokenizer IndependentLines(string fileName, IReadOnlyList<string> lines)
    {
        LanguageSpec spec = LanguageSpec.ForFile(fileName);
        if (spec.IsPlain || lines.Count > MaxLines || TotalChars(lines) > MaxChars) return Plain(lines);
        return new SyntaxTokenizer(spec, lines, new bool[lines.Count]);
    }

    /// <summary>Highlighted fragments of a line, ordered and non-overlapping.</summary>
    public IReadOnlyList<Token> Tokens(int lineIndex)
    {
        if (Language.IsPlain || lineIndex < 0 || lineIndex >= _lines.Count) return [];
        var output = new List<Token>();
        Scan(Language, _lines[lineIndex], _startsInBlock[lineIndex], output);
        return output;
    }

    private static long TotalChars(IReadOnlyList<string> lines)
    {
        long total = 0;
        foreach (string line in lines) total += line.Length;
        return total;
    }

    /// <summary>Walks a line. Returns whether it ends inside a block. If <paramref name="output"/> is null, it only computes that state.</summary>
    private static bool Scan(LanguageSpec spec, string line, bool inBlock, List<Token>? output)
    {
        int n = line.Length;
        int i = 0;
        if (inBlock)
        {
            int end = line.IndexOf(spec.BlockEnd!, 0, StringComparison.Ordinal);
            if (end < 0)
            {
                Add(output, 0, n, spec.BlockType);
                return true;
            }
            i = end + spec.BlockEnd!.Length;
            Add(output, 0, i, spec.BlockType);
        }
        if (spec.Headings && i == 0 && IsHeading(line))
        {
            Add(output, 0, n, TokenType.Keyword);
            return false;
        }
        while (i < n)
        {
            char c = line[i];
            if (spec.LineComment is not null && StartsWithAt(line, spec.LineComment, i))
            {
                Add(output, i, n, TokenType.Comment);
                return false;
            }
            if (spec.BlockStart is not null && StartsWithAt(line, spec.BlockStart, i))
            {
                int from = i + spec.BlockStart.Length;
                int end = line.IndexOf(spec.BlockEnd!, from, StringComparison.Ordinal);
                if (end < 0)
                {
                    Add(output, i, n, spec.BlockType);
                    return true;
                }
                int stop = end + spec.BlockEnd!.Length;
                Add(output, i, stop, spec.BlockType);
                i = stop;
                continue;
            }
            if (spec.Quotes.Contains(c))
            {
                int stop = EndOfString(line, i, c);
                Add(output, i, stop, TokenType.String);
                i = stop;
                continue;
            }
            if (spec.MarkupTags && c == '<' && i + 1 < n && (char.IsLetter(line[i + 1]) || line[i + 1] == '/'))
            {
                int stop = i + 1;
                while (stop < n && (char.IsLetterOrDigit(line[stop]) || ":._-/".Contains(line[stop]))) stop++;
                Add(output, i, stop, TokenType.Keyword);
                i = stop;
                continue;
            }
            if (char.IsDigit(c) && (i == 0 || !IsIdentifierPart(line[i - 1])))
            {
                int stop = i + 1;
                while (stop < n && (char.IsLetterOrDigit(line[stop]) || line[stop] == '.' || line[stop] == '_')) stop++;
                Add(output, i, stop, TokenType.Number);
                i = stop;
                continue;
            }
            if (IsIdentifierStart(c))
            {
                int stop = i + 1;
                while (stop < n && IsIdentifierPart(line[stop])) stop++;
                string word = line[i..stop];
                if (spec.Keywords.Contains(spec.IgnoreCase ? word.ToLowerInvariant() : word))
                {
                    Add(output, i, stop, TokenType.Keyword);
                }
                i = stop;
                continue;
            }
            i++;
        }
        return false;
    }

    /// <summary>Equivalent to Java's <c>String.startsWith(prefix, offset)</c>: ordinal comparison.</summary>
    private static bool StartsWithAt(string line, string prefix, int offset) =>
        line.AsSpan(offset).StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>End of a string that starts at <paramref name="start"/>: the closing quote (with backslash as the escape) or the end of the line.</summary>
    private static int EndOfString(string line, int start, char quote)
    {
        int i = start + 1;
        while (i < line.Length)
        {
            char c = line[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }
            if (c == quote) return i + 1;
            i++;
        }
        return line.Length;
    }

    private static bool IsHeading(string line)
    {
        int hashes = 0;
        while (hashes < line.Length && line[hashes] == '#') hashes++;
        return hashes >= 1 && hashes <= 6 && hashes < line.Length && line[hashes] == ' ';
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_' || c == '$';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    private static void Add(List<Token>? output, int start, int end, TokenType type)
    {
        if (output is not null && end > start) output.Add(new Token(start, end, type));
    }
}
