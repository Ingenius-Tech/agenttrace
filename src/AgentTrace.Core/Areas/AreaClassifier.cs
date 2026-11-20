using System.Collections.Concurrent;

namespace AgentTrace.Core.Areas;

/// <summary>The area id of a file in each dimension, or null where no area matches.</summary>
public sealed record AreaAssignment(string? Layer, string? Module, string? Kind)
{
    public static readonly AreaAssignment None = new(null, null, null);

    public string? Of(AreaDimension dimension) => dimension switch
    {
        AreaDimension.Layer => Layer,
        AreaDimension.Module => Module,
        AreaDimension.Kind => Kind,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
    };
}

/// <summary>
/// Assigns the files of one repository to areas by the first matching rule in each dimension. Globs are compiled once and results are cached per
/// path; safe to call from several threads. Files outside the repository have no area.
/// </summary>
public sealed class AreaClassifier
{
    private readonly FilePath _root;
    private readonly List<Compiled> _rules = [];
    private readonly ConcurrentDictionary<FilePath, AreaAssignment> _cache = new();

    private sealed record Compiled(AreaRule Rule, GlobPattern[] Include, GlobPattern[] Exclude);

    /// <param name="repoRoot">repository root the patterns are relative to</param>
    /// <param name="rules">rules already validated (see <see cref="AreaRules.Validate"/>); a pattern that does not compile is ignored</param>
    public AreaClassifier(FilePath repoRoot, AreaRules rules)
    {
        _root = repoRoot;
        foreach (AreaRule rule in rules.Areas)
        {
            _rules.Add(new Compiled(rule, Compile(rule.Include), Compile(rule.Exclude)));
        }
    }

    public AreaAssignment Classify(FilePath file) => _cache.GetOrAdd(file, Compute);

    private AreaAssignment Compute(FilePath file)
    {
        if (!file.IsUnder(_root) || file == _root) return AreaAssignment.None;
        string relative = file.RelativeTo(_root).Replace('\\', '/');
        string?[] winner = new string?[3];
        foreach (Compiled c in _rules)
        {
            ref string? slot = ref winner[(int)c.Rule.Dimension];
            if (slot is not null) continue;
            if (c.Include.Any(g => g.IsMatch(relative)) && !c.Exclude.Any(g => g.IsMatch(relative))) slot = c.Rule.Id;
        }
        return new AreaAssignment(winner[(int)AreaDimension.Layer], winner[(int)AreaDimension.Module], winner[(int)AreaDimension.Kind]);
    }

    private static GlobPattern[] Compile(IEnumerable<string> patterns) =>
        patterns.Select(p => GlobPattern.TryCreate(p, out _)).OfType<GlobPattern>().ToArray();
}
