namespace AgentTrace.Presentation.Tree;

/// <summary>
/// Family of a file, only to choose its icon. It does not interpret languages: it is a table of common extensions and
/// names, easy to extend; anything unknown is text.
/// </summary>
public enum FileKind { Code, Config, Markup, Image, Archive, Pdf, Binary, Text }

public static class FileKinds
{
    private static readonly HashSet<string> CodeExtensions = ["java", "kt", "kts", "scala", "groovy", "js", "mjs", "cjs",
        "ts", "tsx", "jsx", "c", "h", "cpp", "cc", "hpp", "cs", "go", "rs", "swift", "py", "rb", "php", "sh", "bash",
        "ps1", "bat", "cmd", "sql", "lua", "pl", "r", "dart", "vue", "svelte"];
    private static readonly HashSet<string> ConfigExtensions = ["json", "yaml", "yml", "toml", "xml", "properties", "ini",
        "cfg", "conf", "gradle", "lock", "env"];
    private static readonly HashSet<string> MarkupExtensions = ["md", "markdown", "html", "htm", "css", "scss", "less",
        "rst", "adoc", "tex"];
    private static readonly HashSet<string> ImageExtensions = ["png", "jpg", "jpeg", "gif", "svg", "ico", "bmp", "webp"];
    private static readonly HashSet<string> ArchiveExtensions = ["zip", "jar", "war", "tar", "gz", "tgz", "7z", "rar"];
    private static readonly HashSet<string> BinaryExtensions = ["exe", "dll", "so", "dylib", "class", "bin", "o", "a", "obj",
        "wasm", "pyc"];
    private static readonly Dictionary<string, FileKind> ByName = new()
    {
        ["dockerfile"] = FileKind.Config, ["makefile"] = FileKind.Config, ["pom.xml"] = FileKind.Config,
        [".gitignore"] = FileKind.Config, [".gitattributes"] = FileKind.Config, [".editorconfig"] = FileKind.Config,
        ["readme"] = FileKind.Markup, ["license"] = FileKind.Text,
    };

    public static FileKind Of(string fileName)
    {
        string name = fileName.ToLowerInvariant();
        if (ByName.TryGetValue(name, out FileKind byName)) return byName;
        int dot = name.LastIndexOf('.');
        string extension = dot < 0 ? "" : name[(dot + 1)..];
        if (CodeExtensions.Contains(extension)) return FileKind.Code;
        if (ConfigExtensions.Contains(extension)) return FileKind.Config;
        if (MarkupExtensions.Contains(extension)) return FileKind.Markup;
        if (ImageExtensions.Contains(extension)) return FileKind.Image;
        if (ArchiveExtensions.Contains(extension)) return FileKind.Archive;
        if (BinaryExtensions.Contains(extension)) return FileKind.Binary;
        if (extension == "pdf") return FileKind.Pdf;
        return FileKind.Text;
    }
}
