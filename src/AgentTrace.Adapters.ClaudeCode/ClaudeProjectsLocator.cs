using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using AgentTrace.Core;

namespace AgentTrace.Adapters.ClaudeCode;

/// <summary>
/// Locates the traces of a project in <c>~/.claude/projects</c>. Only reads the file system.
/// Claude Code names a project's folder by replacing every non-alphanumeric character of its path with '-'.
/// That rule loses information, so if the expected folder does not exist, the search falls back to the <c>cwd</c> field
/// of the traces.
/// </summary>
internal sealed partial class ClaudeProjectsLocator(FilePath root, bool caseInsensitive)
{
    private const int CwdScanLines = 50;

    public FilePath Root { get; } = root;

    /// <summary>The user's <c>projects</c> folder: <c>$CLAUDE_CONFIG_DIR/projects</c> or <c>~/.claude/projects</c>.</summary>
    public static FilePath DefaultRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        string baseDir = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return new FilePath(Path.Combine(baseDir, "projects"));
    }

    public static bool DefaultCaseInsensitive() => OperatingSystem.IsWindows();

    /// <summary>Full <c>agent-.+\.jsonl</c>; Java's dot does not match any line terminator.</summary>
    public static bool IsSubagentFileName(string name) => SubagentFile().IsMatch(name);

    // Zl and Zp are only U+2028 and U+2029
    [GeneratedRegex(@"\Aagent-[^\n\r\u0085\p{Zl}\p{Zp}]+\.jsonl\z")]
    private static partial Regex SubagentFile();

    /// <summary>Every character that is not an ASCII letter or digit becomes '-' (per UTF-16 unit, as in Java).</summary>
    public static string Encode(string absolutePath)
    {
        var sb = new StringBuilder(absolutePath.Length);
        foreach (char c in absolutePath) sb.Append(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-');
        return sb.ToString();
    }

    /// <summary>Trace folder of a project, if it exists.</summary>
    public FilePath? ProjectFolder(FilePath projectDir)
    {
        if (!Directory.Exists(Root.Value)) return null;
        string name = Encode(projectDir.Value);
        FilePath exact = Root.Combine(name);
        if (Directory.Exists(exact.Value)) return exact;
        try
        {
            List<FilePath> candidates = Directory.EnumerateDirectories(Root.Value).Select(d => new FilePath(d)).ToList();
            if (caseInsensitive)
            {
                foreach (FilePath candidate in candidates)
                {
                    if (string.Equals(candidate.FileName, name, StringComparison.OrdinalIgnoreCase)) return candidate;
                }
            }
            foreach (FilePath candidate in candidates)
            {
                if (DeclaresCwd(candidate, projectDir)) return candidate;
            }
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Sessions of the project, from the most recently modified to the oldest.</summary>
    public IReadOnlyList<FilePath> FindSessionFiles(FilePath projectDir)
    {
        FilePath? folder = ProjectFolder(projectDir);
        if (folder is null) return [];
        try
        {
            return Directory.EnumerateFiles(folder.Value.Value)
                .Where(p => p.EndsWith(".jsonl", StringComparison.Ordinal))
                .Select(p => new FilePath(p))
                .OrderByDescending(LastModified)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Subagents of a session: <c>&lt;session&gt;/subagents/agent-*.jsonl</c>.</summary>
    public IReadOnlyList<FilePath> FindSubagentFiles(FilePath sessionFile)
    {
        string name = sessionFile.FileName;
        if (name.EndsWith(".jsonl", StringComparison.Ordinal)) name = name[..^".jsonl".Length];
        FilePath dir = (sessionFile.Parent ?? sessionFile).Combine(name, "subagents");
        if (!Directory.Exists(dir.Value)) return [];
        try
        {
            return Directory.EnumerateFiles(dir.Value)
                .Where(s => IsSubagentFileName(Path.GetFileName(s)))
                .Select(s => new FilePath(s))
                .OrderBy(LastModified)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static long LastModified(FilePath p)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(p.Value), TimeSpan.Zero).ToUnixTimeMilliseconds();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private bool DeclaresCwd(FilePath folder, FilePath projectDir)
    {
        try
        {
            FilePath? newest = Directory.EnumerateFiles(folder.Value)
                .Where(f => Path.GetFileName(f).EndsWith(".jsonl", StringComparison.Ordinal))
                .Select(f => new FilePath(f))
                .MaxBy(LastModified);
            if (newest is null) return false;
            // Strict UTF-8: as in Java, an invalid byte is a read error and the folder does not count
            using var reader = new StreamReader(newest.Value.Value, new UTF8Encoding(false, true));
            for (int i = 0; i < CwdScanLines && reader.ReadLine() is { } line; i++)
            {
                string? cwd = ReadCwd(line);
                if (cwd is not null) return SamePath(cwd, projectDir);
            }
        }
        catch (Exception)
        {
            return false;
        }
        return false;
    }

    private static string? ReadCwd(string line)
    {
        try
        {
            using JsonDocument? doc = JsonPath.Parse(line);
            JsonElement cwd = doc is null ? default : doc.RootElement.Path("cwd");
            return cwd.IsTextual() ? cwd.AsText() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private bool SamePath(string a, FilePath b) =>
        string.Equals(new FilePath(a).Value, b.Value, caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
