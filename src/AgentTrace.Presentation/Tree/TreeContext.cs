using AgentTrace.Core;
using AgentTrace.Ingest;

namespace AgentTrace.Presentation.Tree;

/// <summary>What the tree needs to know to list and paint: what is ignored, the status of each file and whether only the changes are shown.</summary>
public sealed class TreeContext(IgnoreRules rules, FileStatusIndex index)
{
    private volatile IgnoreRules _rules = rules;
    private volatile FileStatusIndex _index = index;

    public IgnoreRules Rules => _rules;

    public FileStatusIndex Index => _index;

    public volatile bool ChangedOnly;

    public void Update(IgnoreRules newRules, FileStatusIndex newIndex)
    {
        _rules = newRules;
        _index = newIndex;
    }
}
