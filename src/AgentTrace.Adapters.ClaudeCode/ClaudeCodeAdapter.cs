using System.Text.RegularExpressions;
using AgentTrace.Core;

namespace AgentTrace.Adapters.ClaudeCode;

/// <summary>Claude Code (CLI) adapter. Reads its JSONL traces from <c>~/.claude/projects</c>.</summary>
public sealed partial class ClaudeCodeAdapter : IAgentAdapter
{
    private readonly ClaudeProjectsLocator _locator;

    /// <summary>The user's trace folder (<c>$CLAUDE_CONFIG_DIR/projects</c> or <c>~/.claude/projects</c>).</summary>
    public ClaudeCodeAdapter()
        : this(ClaudeProjectsLocator.DefaultRoot(), ClaudeProjectsLocator.DefaultCaseInsensitive())
    {
    }

    internal ClaudeCodeAdapter(FilePath projectsRoot, bool caseInsensitive) =>
        _locator = new ClaudeProjectsLocator(projectsRoot, caseInsensitive);

    public string Agent => ClaudeCodeLineParser.AgentName;

    public bool Owns(FilePath traceFile)
    {
        string name = traceFile.FileName;
        return traceFile.IsUnder(_locator.Root) && (SessionFile().IsMatch(name) || ClaudeProjectsLocator.IsSubagentFileName(name));
    }

    public IReadOnlyList<FilePath> FindTraceFiles(FilePath projectDir) => _locator.FindSessionFiles(projectDir);

    public IReadOnlyList<FilePath> RelatedTraceFiles(FilePath sessionTrace) => _locator.FindSubagentFiles(sessionTrace);

    public ILineParser NewParser(FilePath traceFile) => new ClaudeCodeLineParser(traceFile);

    [GeneratedRegex(@"\A[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\.jsonl\z")]
    private static partial Regex SessionFile();
}
