using System.Diagnostics;

namespace AgentTrace.Core;

/// <summary>Who made a change, according to attribution.</summary>
public enum Actor
{
    /// <summary>The trace explains it (exact, probable or ambiguous among agent actions).</summary>
    Agent,

    /// <summary>No agent action explains it: the user or another process.</summary>
    External,

    /// <summary>Cannot be determined (a deleted file has no modification time).</summary>
    Unknown,
}

public static class Actors
{
    public static Actor Of(Attribution attribution) => attribution.Confidence switch
    {
        Confidence.Exact or Confidence.Probable or Confidence.Ambiguous => Actor.Agent,
        Confidence.None => Actor.External,
        Confidence.Undated => Actor.Unknown,
        _ => throw new UnreachableException(),
    };
}
