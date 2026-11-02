using System.Diagnostics;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Core;

/// <summary>Accumulated summary of the events of a session. Not thread-safe: use from a single thread.</summary>
public sealed class SessionStats
{
    private long _events;
    private long _turns;
    private TimeSpan _turnTime = TimeSpan.Zero;
    private long _inputTokens;
    private long _outputTokens;
    private long _cacheReadTokens;
    private long _cacheCreationTokens;
    private long _toolErrors;
    private readonly SortedDictionary<string, long> _toolCalls = new(StringComparer.Ordinal);
    private readonly Dictionary<SkipReason, long> _skipped = [];
    private readonly HashSet<FilePath> _filesTouched = [];
    private readonly HashSet<FilePath> _filesChangedExternally = [];
    private readonly HashSet<FilePath> _filesRead = [];
    private readonly HashSet<string> _subagents = [];

    public void Accept(AgentEvent evt)
    {
        _events++;
        switch (evt)
        {
            case ToolCall e:
                _toolCalls[e.ToolName] = _toolCalls.GetValueOrDefault(e.ToolName) + 1;
                break;
            case ToolResult e:
                if (e.Error) _toolErrors++;
                break;
            case FileChanged e:
                _filesTouched.Add(e.Path);
                if (e.CallId == null) _filesChangedExternally.Add(e.Path);
                break;
            case FileRead e:
                _filesRead.Add(e.Path);
                break;
            case SubagentStarted e:
                _subagents.Add(e.SubagentId);
                break;
            case TokenUsage e:
                _inputTokens += e.Input;
                _outputTokens += e.Output;
                _cacheReadTokens += e.CacheRead;
                _cacheCreationTokens += e.CacheCreation;
                break;
            case TurnEnded e:
                _turns++;
                if (e.Duration is { } duration) _turnTime += duration;
                break;
            case Skipped e:
                _skipped[e.Reason] = _skipped.GetValueOrDefault(e.Reason) + 1;
                break;
            case UserMessage or AssistantText or Thinking:
                break;
            default:
                throw new UnreachableException();
        }
    }

    public long Events => _events;
    public long Turns => _turns;
    public TimeSpan TurnTime => _turnTime;
    public long InputTokens => _inputTokens;
    public long OutputTokens => _outputTokens;
    public long CacheReadTokens => _cacheReadTokens;
    public long CacheCreationTokens => _cacheCreationTokens;
    public long ToolErrors => _toolErrors;

    /// <summary>Calls per tool, sorted by name (ordinal comparison, like <c>TreeMap</c>).</summary>
    public IReadOnlyDictionary<string, long> ToolCalls => new SortedDictionary<string, long>(_toolCalls, StringComparer.Ordinal);

    public long SkippedCount(SkipReason reason) => _skipped.GetValueOrDefault(reason);

    public IReadOnlySet<FilePath> FilesTouched => new HashSet<FilePath>(_filesTouched);
    public IReadOnlySet<FilePath> FilesChangedExternally => new HashSet<FilePath>(_filesChangedExternally);
    public IReadOnlySet<FilePath> FilesRead => new HashSet<FilePath>(_filesRead);
    public IReadOnlySet<string> Subagents => new HashSet<string>(_subagents);

    public long TotalToolCalls => _toolCalls.Values.Sum();
}
