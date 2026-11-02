using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentTrace.Core;
using static AgentTrace.Core.AgentEvent;

namespace AgentTrace.Adapters.ClaudeCode;

/// <summary>
/// Interprets the lines of a Claude Code JSONL trace. It is tolerant: anything it does not know
/// produces <see cref="Skipped"/> and it never throws. It is stateful (one instance per file):
/// it remembers the <c>message.id</c>s already counted (the assistant writes one line per block and repeats
/// the token usage on all of them) and the name of each tool called.
/// </summary>
internal sealed partial class ClaudeCodeLineParser : ILineParser
{
    internal const string AgentName = "claude-code";

    /// <summary>Line types that are recognized but do not yield an event of their own.</summary>
    private static readonly HashSet<string> IgnoredTypes =
    [
        "last-prompt", "mode", "permission-mode", "atis-latch", "ai-title", "custom-title", "tools-used",
        "api-request", "api-request-shape", "api-request-blob", "file-history-snapshot",
        "file-history-delta", "queue-operation", "cost-state", "pr-link", "fork-context-ref",
    ];

    /// <summary>File-reading tools: their path is in the call input (the result of an image does not carry it).</summary>
    private static readonly HashSet<string> FileReadTools = ["Read"];

    private readonly FilePath _file;
    private readonly SessionId _defaultSession;
    private readonly string? _subagentId;
    private readonly HashSet<string> _countedMessageIds = [];
    private readonly Dictionary<string, string> _toolNames = [];
    private readonly Dictionary<string, string> _readTargets = [];

    /// <summary>Edit calls whose text to replace is empty (they create the file).</summary>
    private readonly HashSet<string> _emptyOldStrings = [];

    /// <summary>Calls that delegate to a subagent, with the description of what was delegated.</summary>
    private readonly Dictionary<string, string> _delegations = [];

    private bool _metaEmitted;
    private int _metaAttempts;

    public ClaudeCodeLineParser(FilePath file)
    {
        _file = file;
        string name = file.FileName;
        if (name.EndsWith(".jsonl", StringComparison.Ordinal)) name = name[..^".jsonl".Length];
        FilePath? parent = file.Parent;
        if (name.StartsWith("agent-", StringComparison.Ordinal) && parent is { } dir
            && dir.FileName == "subagents" && dir.Parent is { } sessionDir)
        {
            _subagentId = name["agent-".Length..];
            _defaultSession = new SessionId(sessionDir.FileName);
        }
        else
        {
            _subagentId = null;
            _defaultSession = new SessionId(name);
        }
    }

    public IReadOnlyList<AgentEvent> Parse(string line, long lineNumber)
    {
        EventHeader fallback = Header(default, lineNumber);
        try
        {
            using JsonDocument? doc = JsonPath.Parse(line);
            JsonElement root = doc is null ? default : doc.RootElement;
            if (!root.IsObject()) return [new Skipped(fallback, "?", SkipReason.Malformed)];
            EventHeader h = Header(root, lineNumber);
            string type = root.Path("type").AsText("");
            var output = new List<AgentEvent>();
            if (root.Path("isMeta").AsBoolean(false))
            {
                output.Add(new Skipped(h, type, SkipReason.Ignored));
            }
            else
            {
                switch (type)
                {
                    case "user": ParseUser(root, h, output); break;
                    case "assistant": ParseAssistant(root, h, output); break;
                    case "attachment": ParseAttachment(root, h, output); break;
                    case "system": ParseSystem(root, h, output); break;
                }
                if (output.Count == 0)
                {
                    output.Add(new Skipped(h, type, IgnoredTypes.Contains(type) || IsHandled(type) ? SkipReason.Ignored : SkipReason.Unknown));
                }
            }
            AddSubagentLink(h, output);
            return output;
        }
        catch (Exception)
        {
            return [new Skipped(fallback, "?", SkipReason.Malformed)];
        }
    }

    private static bool IsHandled(string type) => type is "user" or "assistant" or "attachment" or "system";

    private EventHeader Header(JsonElement root, long lineNumber)
    {
        SessionId session = _defaultSession;
        DateTimeOffset? ts = null;
        string s = root.Path("sessionId").AsText("");
        if (s.Length > 0) session = new SessionId(s);
        string t = root.Path("timestamp").AsText("");
        if (t.Length > 0) ts = ParseInstant(t); // unreadable timestamp: left as null
        return new EventHeader(session, AgentName, _subagentId, ts, new SourceRef(_file, lineNumber));
    }

    private void ParseUser(JsonElement root, EventHeader h, List<AgentEvent> output)
    {
        JsonElement content = root.Path("message").Path("content");
        if (content.IsTextual())
        {
            output.Add(new UserMessage(h, content.AsText()));
            return;
        }
        foreach (JsonElement block in content.Elements())
        {
            switch (block.Path("type").AsText(""))
            {
                case "tool_result": ParseToolResult(root, block, h, output); break;
                case "text": output.Add(new UserMessage(h, block.Path("text").AsText(""))); break;
            }
        }
    }

    private void ParseToolResult(JsonElement root, JsonElement block, EventHeader h, List<AgentEvent> output)
    {
        string callId = block.Path("tool_use_id").AsText("");
        bool error = block.Path("is_error").AsBoolean(false);
        output.Add(new ToolResult(h, callId, error, Flatten(block.Path("content"))));
        if (!error && _readTargets.TryGetValue(callId, out string? readTarget)) output.Add(new FileRead(h, new FilePath(readTarget), callId));
        string agentId = root.Path("toolUseResult").Path("agentId").AsText("");
        if (!error && _delegations.TryGetValue(callId, out string? delegated) && agentId.Length > 0)
        {
            output.Add(new SubagentStarted(h, callId, agentId, delegated));
        }
        // A result whose call was not seen (another file, compaction) is still a result
        if (error || !_toolNames.TryGetValue(callId, out string? tool) || !ClaudeTools.WritesFiles(tool)) return;
        JsonElement result = root.Path("toolUseResult");
        string path = result.Path("filePath").AsText(result.Path("notebook_path").AsText(""));
        if (path.Length == 0) return;
        bool created = IsCreation(tool, callId, result);
        output.Add(new FileChanged(h, new FilePath(path), created ? ChangeKind.Created : ChangeKind.Modified, callId));
    }

    /// <summary>
    /// Rule order: the explicit <c>type</c> of the result wins; without it, <c>Write</c> creates if there was no <c>originalFile</c>
    /// and the other tools only create if the edit started from an empty text. When in doubt, modified (created is the strong claim).
    /// </summary>
    private bool IsCreation(string tool, string callId, JsonElement result)
    {
        switch (result.Path("type").AsText(""))
        {
            case "create": return true;
            case "update": return false;
        }
        if (tool == "Write") return result.Path("originalFile").IsNull() || result.Path("originalFile").IsMissing();
        return _emptyOldStrings.Contains(callId);
    }

    private void ParseAssistant(JsonElement root, EventHeader h, List<AgentEvent> output)
    {
        JsonElement message = root.Path("message");
        foreach (JsonElement block in message.Path("content").Elements())
        {
            switch (block.Path("type").AsText(""))
            {
                case "text":
                    output.Add(new AssistantText(h, block.Path("text").AsText("")));
                    break;
                case "thinking":
                    JsonElement ms = root.Path("thinkingDurationMs");
                    TimeSpan? d = ms.CanConvertToLong() ? Millis(ms.AsLong(0)) : null;
                    output.Add(new Thinking(h, block.Path("thinking").AsText(""), d));
                    break;
                case "tool_use":
                    string id = block.Path("id").AsText("");
                    string name = block.Path("name").AsText("");
                    _toolNames[id] = name;
                    string target = block.Path("input").Path("file_path").AsText("");
                    if (FileReadTools.Contains(name) && target.Length > 0) _readTargets[id] = target;
                    if (StartsFromEmptyText(name, block.Path("input"))) _emptyOldStrings.Add(id);
                    ToolKind kind = ClaudeTools.KindOf(name);
                    string summary = ClaudeTools.Summary(kind, block.Path("input"));
                    if (kind == ToolKind.Delegate) _delegations[id] = summary;
                    string inputTarget = block.Path("input").Path("file_path").AsText(block.Path("input").Path("notebook_path").AsText(""));
                    FilePath? callTarget = kind is ToolKind.Read or ToolKind.Write && inputTarget.Length > 0 ? new FilePath(inputTarget) : null;
                    output.Add(new ToolCall(h, id, name, kind, summary, block.Path("input").ToJsonString()) { Target = callTarget });
                    break;
            }
        }
        string messageId = message.Path("id").AsText("");
        JsonElement usage = message.Path("usage");
        if (messageId.Length > 0 && usage.IsObject() && _countedMessageIds.Add(messageId))
        {
            output.Add(new TokenUsage(h, message.Path("model").AsText(""),
                usage.Path("input_tokens").AsLong(0), usage.Path("output_tokens").AsLong(0),
                usage.Path("cache_read_input_tokens").AsLong(0),
                usage.Path("cache_creation_input_tokens").AsLong(0)));
        }
    }

    private static bool StartsFromEmptyText(string tool, JsonElement input)
    {
        if (tool == "Edit") return input.Path("old_string").IsTextual() && input.Path("old_string").AsText() == "";
        if (tool != "MultiEdit") return false;
        JsonElement first = input.Path("edits").Elements().FirstOrDefault();
        return first.IsObject() && first.Path("old_string").IsTextual() && first.Path("old_string").AsText() == "";
    }

    /// <summary>
    /// In a subagent's file, its sibling <c>meta.json</c> says which call launched it and why. It is read only once
    /// (with a few retries in case it does not exist yet) and covers the case where the call's result arrives last.
    /// </summary>
    private void AddSubagentLink(EventHeader h, List<AgentEvent> output)
    {
        if (_subagentId is null || _metaEmitted || _metaAttempts >= 20) return;
        _metaAttempts++;
        string meta = Path.Combine(Path.GetDirectoryName(_file.Value) ?? "", "agent-" + _subagentId + ".meta.json");
        try
        {
            if (!File.Exists(meta)) return;
            using JsonDocument? doc = JsonPath.Parse(File.ReadAllText(meta));
            JsonElement node = doc is null ? default : doc.RootElement;
            output.Add(new SubagentStarted(h, node.Path("toolUseId").AsText(""), _subagentId, node.Path("description").AsText("")));
            _metaEmitted = true;
        }
        catch (Exception)
        {
            // an unreadable or half-written meta.json is retried on the next line
        }
    }

    private static void ParseAttachment(JsonElement root, EventHeader h, List<AgentEvent> output)
    {
        JsonElement attachment = root.Path("attachment");
        if (attachment.Path("type").AsText("") != "edited_text_file") return;
        string filename = attachment.Path("filename").AsText("");
        if (filename.Length > 0) output.Add(new FileChanged(h, new FilePath(filename), ChangeKind.Modified, null));
    }

    private static void ParseSystem(JsonElement root, EventHeader h, List<AgentEvent> output)
    {
        if (root.Path("subtype").AsText("") != "turn_duration") return;
        JsonElement ms = root.Path("durationMs");
        output.Add(new TurnEnded(h, ms.CanConvertToLong() ? Millis(ms.AsLong(0)) : null));
    }

    /// <summary>The content of a result can be text or a list of <c>{type:text}</c>.</summary>
    private static string Flatten(JsonElement content)
    {
        if (content.IsTextual()) return content.AsText();
        if (content.IsArray())
        {
            var sb = new StringBuilder();
            foreach (JsonElement part in content.Elements())
            {
                if (sb.Length > 0) sb.Append('\n');
                if (part.IsTextual()) sb.Append(part.AsText());
                else if (part.Path("type").AsText("") == "image") sb.Append("[image]");
                else sb.Append(part.Path("text").AsText(part.ToJsonString()));
            }
            return sb.ToString();
        }
        return content.IsMissing() || content.IsNull() ? "" : content.ToJsonString();
    }

    /// <summary><c>Duration.ofMillis</c>: any <c>long</c> is valid, so what does not fit in a <see cref="TimeSpan"/> saturates.</summary>
    private static TimeSpan Millis(long ms)
    {
        try
        {
            return TimeSpan.FromTicks(checked(ms * TimeSpan.TicksPerMillisecond));
        }
        catch (OverflowException)
        {
            return ms < 0 ? TimeSpan.MinValue : TimeSpan.MaxValue;
        }
    }

    /// <summary>
    /// <c>Instant.parse</c> from Java (ISO-8601 with seconds, a fraction of up to 9 digits and <c>Z</c> or an offset
    /// <c>±hh:mm[:ss]</c>, case-insensitive), in UTC; null if the text does not match. The fraction is truncated to 100 ns.
    /// </summary>
    private static DateTimeOffset? ParseInstant(string text)
    {
        Match m = InstantText().Match(text);
        if (!m.Success) return null;
        try
        {
            int G(int i) => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
            var utc = new DateTimeOffset(G(1), G(2), G(3), G(4), G(5), G(6), TimeSpan.Zero);
            if (m.Groups[7].Success)
            {
                string fraction = (m.Groups[7].Value + "000000000")[..7];
                utc = utc.AddTicks(long.Parse(fraction, CultureInfo.InvariantCulture));
            }
            string offset = m.Groups[8].Value;
            if (offset is not ("Z" or "z"))
            {
                int sign = offset[0] == '-' ? -1 : 1;
                int seconds = int.Parse(offset.Substring(1, 2), CultureInfo.InvariantCulture) * 3600
                    + int.Parse(offset.Substring(4, 2), CultureInfo.InvariantCulture) * 60
                    + (offset.Length > 6 ? int.Parse(offset.Substring(7, 2), CultureInfo.InvariantCulture) : 0);
                utc = utc.AddSeconds(-sign * seconds);
            }
            return utc;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\A([0-9]{4})-([0-9]{2})-([0-9]{2})T([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\.([0-9]{1,9}))?(Z|[+-][0-9]{2}:[0-9]{2}(?::[0-9]{2})?)\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstantText();
}
