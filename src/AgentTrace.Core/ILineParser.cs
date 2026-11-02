namespace AgentTrace.Core;

/// <summary>Converts the lines of a specific trace into events. Stateful: one is created per file.</summary>
public interface ILineParser
{
    /// <summary>Never throws: an unreadable line produces <see cref="AgentEvent.Skipped"/>.</summary>
    IReadOnlyList<AgentEvent> Parse(string line, long lineNumber);
}
