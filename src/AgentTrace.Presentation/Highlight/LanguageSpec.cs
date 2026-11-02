namespace AgentTrace.Presentation.Highlight;

/// <summary>
/// How a family of languages is highlighted. It is a small table, presentation only: neither the core nor the adapter know
/// about languages. Anything not recognized is shown as plain text.
/// <c>LineComment</c>: start of a comment up to the end of the line, or null. <c>BlockStart</c>: start of a block that may
/// span several lines (a comment or, in Markdown, code), or null. <c>BlockEnd</c>: end of that block.
/// <c>BlockType</c>: type of the block (Comment in most; String in the code blocks of Markdown).
/// <c>Quotes</c>: characters that open and close a string (with backslash as the escape).
/// <c>IgnoreCase</c>: keywords are case-insensitive. <c>MarkupTags</c>: highlight <c>&lt;name</c> tags
/// (XML and HTML). <c>Headings</c>: lines that start with # and a space are headings (Markdown).
/// </summary>
public sealed record LanguageSpec(string Name, IReadOnlySet<string> Keywords, string? LineComment, string? BlockStart, string? BlockEnd,
    TokenType BlockType, string Quotes, bool IgnoreCase, bool MarkupTags, bool Headings)
{
    private static readonly HashSet<string> NoKeywords = [];

    public static readonly LanguageSpec Plain = new("texto", NoKeywords, null, null, null, TokenType.Comment, "", false, false, false);

    private static readonly HashSet<string> Jvm = ["abstract", "as", "assert", "boolean", "break", "byte", "case", "catch", "char",
        "class", "const", "continue", "def", "default", "do", "double", "else", "enum", "extends", "false", "final", "finally",
        "float", "for", "fun", "if", "implements", "import", "in", "instanceof", "int", "interface", "is", "long", "new", "null",
        "object", "override", "package", "private", "protected", "public", "record", "return", "sealed", "short", "static",
        "super", "switch", "synchronized", "this", "throw", "throws", "trait", "true", "try", "val", "var", "void", "when",
        "while", "yield", "namespace", "using", "internal", "readonly", "string", "lazy", "implicit"];
    private static readonly HashSet<string> Js = ["async", "await", "break", "case", "catch", "class", "const", "continue",
        "debugger", "default", "delete", "do", "else", "enum", "export", "extends", "false", "finally", "for", "from",
        "function", "if", "implements", "import", "in", "instanceof", "interface", "let", "new", "null", "of", "private",
        "protected", "public", "readonly", "return", "static", "super", "switch", "this", "throw", "true", "try", "type",
        "typeof", "undefined", "var", "void", "while", "with", "yield", "abstract", "as", "declare", "namespace", "number",
        "string", "boolean", "any"];
    private static readonly HashSet<string> Systems = ["auto", "break", "case", "char", "class", "const", "continue", "default",
        "defer", "do", "double", "else", "enum", "extern", "false", "fn", "for", "func", "go", "if", "impl", "import", "inline",
        "int", "interface", "let", "long", "loop", "match", "mod", "mut", "namespace", "new", "nil", "null", "pub", "return",
        "self", "short", "signed", "sizeof", "static", "struct", "switch", "template", "this", "trait", "true", "type",
        "typedef", "union", "unsigned", "use", "var", "virtual", "void", "volatile", "while", "where", "package", "range", "map",
        "chan", "select", "final", "override", "guard", "protocol", "extension", "init", "throws", "try", "catch", "throw"];
    private static readonly HashSet<string> Python = ["and", "as", "assert", "async", "await", "break", "class", "continue", "def",
        "del", "elif", "else", "except", "False", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", // lang-ok: Python keyword
        "None", "nonlocal", "not", "or", "pass", "raise", "return", "True", "try", "while", "with", "yield", "self"];
    private static readonly HashSet<string> Shell = ["if", "then", "else", "elif", "fi", "for", "while", "until", "do", "done",
        "case", "esac", "in", "function", "return", "exit", "local", "export", "echo", "set", "unset", "source", "select"];
    private static readonly HashSet<string> Ruby = ["alias", "and", "begin", "break", "case", "class", "def", "do", "else", "elsif",
        "end", "ensure", "false", "for", "if", "in", "module", "next", "nil", "not", "or", "redo", "rescue", "retry", "return",
        "self", "super", "then", "true", "undef", "unless", "until", "when", "while", "yield", "require", "attr_accessor"];
    private static readonly HashSet<string> Config = ["true", "false", "null", "yes", "no", "on", "off"];
    private static readonly HashSet<string> Json = ["true", "false", "null"];
    private static readonly HashSet<string> Sql = ["select", "from", "where", "and", "or", "not", "null", "insert", "into", "values",
        "update", "set", "delete", "create", "table", "alter", "drop", "index", "view", "join", "inner", "left", "right",
        "outer", "full", "on", "as", "group", "by", "order", "having", "limit", "offset", "distinct", "union", "all", "case",
        "when", "then", "else", "end", "in", "is", "like", "between", "exists", "primary", "key", "foreign", "references",
        "default", "constraint", "unique", "begin", "commit", "rollback", "with", "asc", "desc", "count", "sum", "avg",
        "min", "max"];
    private static readonly HashSet<string> PowerShell = ["begin", "break", "catch", "class", "continue", "data", "do", "dynamicparam",
        "else", "elseif", "end", "exit", "filter", "finally", "for", "foreach", "from", "function", "if", "in", "param",
        "process", "return", "switch", "throw", "trap", "try", "until", "using", "var", "while", "workflow", "true", "false",
        "null"];

    private static LanguageSpec CLike(string name, HashSet<string> keywords) =>
        new(name, keywords, "//", "/*", "*/", TokenType.Comment, "\"'`", false, false, false);

    private static LanguageSpec Hash(string name, HashSet<string> keywords) =>
        new(name, keywords, "#", null, null, TokenType.Comment, "\"'", false, false, false);

    private static LanguageSpec Markup(string name) =>
        new(name, NoKeywords, null, "<!--", "-->", TokenType.Comment, "\"'", false, true, false);

    private static readonly Dictionary<string, LanguageSpec> ByExtension = new()
    {
        ["java"] = CLike("java", Jvm), ["kt"] = CLike("kotlin", Jvm), ["kts"] = CLike("kotlin", Jvm),
        ["scala"] = CLike("scala", Jvm), ["groovy"] = CLike("groovy", Jvm), ["cs"] = CLike("c#", Jvm),
        ["js"] = CLike("javascript", Js), ["mjs"] = CLike("javascript", Js), ["cjs"] = CLike("javascript", Js),
        ["ts"] = CLike("typescript", Js), ["tsx"] = CLike("typescript", Js), ["jsx"] = CLike("javascript", Js),
        ["c"] = CLike("c", Systems), ["h"] = CLike("c", Systems), ["cpp"] = CLike("c++", Systems),
        ["cc"] = CLike("c++", Systems), ["hpp"] = CLike("c++", Systems), ["go"] = CLike("go", Systems),
        ["rs"] = CLike("rust", Systems), ["swift"] = CLike("swift", Systems), ["dart"] = CLike("dart", Systems),
        ["py"] = Hash("python", Python), ["sh"] = Hash("shell", Shell), ["bash"] = Hash("shell", Shell),
        ["rb"] = Hash("ruby", Ruby), ["yml"] = Hash("yaml", Config), ["yaml"] = Hash("yaml", Config),
        ["toml"] = Hash("toml", Config), ["properties"] = Hash("properties", NoKeywords),
        ["conf"] = Hash("config", Config), ["ini"] = Hash("ini", Config), ["cfg"] = Hash("config", Config),
        ["env"] = Hash("env", NoKeywords), ["gitignore"] = Hash("gitignore", NoKeywords),
        ["json"] = new LanguageSpec("json", Json, null, null, null, TokenType.Comment, "\"", false, false, false),
        ["xml"] = Markup("xml"), ["html"] = Markup("html"), ["htm"] = Markup("html"),
        ["svg"] = Markup("svg"), ["fxml"] = Markup("fxml"),
        ["sql"] = new LanguageSpec("sql", Sql, "--", "/*", "*/", TokenType.Comment, "'\"", true, false, false),
        ["css"] = new LanguageSpec("css", NoKeywords, null, "/*", "*/", TokenType.Comment, "\"'", false, false, false),
        ["scss"] = new LanguageSpec("scss", NoKeywords, "//", "/*", "*/", TokenType.Comment, "\"'", false, false, false),
        ["md"] = new LanguageSpec("markdown", NoKeywords, null, "```", "```", TokenType.String, "`", false, false, true),
        ["markdown"] = new LanguageSpec("markdown", NoKeywords, null, "```", "```", TokenType.String, "`", false, false, true),
        ["ps1"] = new LanguageSpec("powershell", PowerShell, "#", "<#", "#>", TokenType.Comment, "\"'", true, false, false),
    };

    /// <summary>The language family of a file by its extension (or by its whole name if it carries one), or plain text.</summary>
    public static LanguageSpec ForFile(string fileName)
    {
        string name = fileName.ToLowerInvariant();
        if (name == "dockerfile" || name == "makefile") return Hash(name, NoKeywords);
        int dot = name.LastIndexOf('.');
        string extension = dot < 0 ? "" : name[(dot + 1)..];
        return ByExtension.GetValueOrDefault(extension, Plain);
    }

    /// <summary>True only for the <see cref="Plain"/> instance (in Java, <c>this == PLAIN</c>).</summary>
    public bool IsPlain => ReferenceEquals(this, Plain);
}
