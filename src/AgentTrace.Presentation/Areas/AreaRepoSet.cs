using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Ingest.Areas;
using AgentTrace.Presentation.Tree;

namespace AgentTrace.Presentation.Areas;

/// <summary>Where the rules of a repository come from: its <c>areas.json</c>, the suggested ones (no file), or the suggested ones because the file is invalid.</summary>
public enum AreaSource { File, Suggested, Invalid }

/// <summary>The rules that classify one repository. <c>Errors</c> is empty unless <c>Source</c> is <see cref="AreaSource.Invalid"/>.</summary>
public sealed record AreaRepo(FilePath Root, string Name, AreaRules Rules, AreaClassifier Classifier, AreaSource Source, ValueList<AreaRuleError> Errors)
{
    public AreaRule? Find(string id) => Rules.Areas.FirstOrDefault(a => a.Id == id);
}

/// <summary>
/// The repositories of the project, each with the rules that classify it. A path is classified with the rules of the deepest repository root
/// that contains it (the same rule as <c>ProjectObserver.Versions</c>); a path in no repository has no area.
/// </summary>
public sealed class AreaRepoSet
{
    public static readonly AreaRepoSet Empty = new([]);

    private readonly IReadOnlyList<AreaRepo> _deepestFirst;

    public AreaRepoSet(IEnumerable<AreaRepo> repos)
    {
        Repos = [.. repos.OrderBy(r => r.Root.Value, StringComparer.Ordinal)];
        _deepestFirst = [.. Repos.OrderByDescending(r => r.Root.Value.Length)];
    }

    /// <summary>The repositories, ordered by path.</summary>
    public IReadOnlyList<AreaRepo> Repos { get; }

    /// <summary>The dimension to show first: layers if some rules file defines them, otherwise modules (the suggested rules have no layers).</summary>
    public AreaDimension DefaultDimension =>
        Repos.Any(r => r.Source == AreaSource.File && r.Rules.Areas.Any(a => a.Dimension == AreaDimension.Layer)) ? AreaDimension.Layer : AreaDimension.Module;

    public AreaRepo? RepoOf(FilePath path) => _deepestFirst.FirstOrDefault(r => path.IsUnder(r.Root) || path == r.Root);

    /// <summary>The repository and the areas of a path; the repository is null for a path in none.</summary>
    public (AreaRepo? Repo, AreaAssignment Assignment) Classify(FilePath path) =>
        RepoOf(path) is { } repo ? (repo, repo.Classifier.Classify(path)) : (null, AreaAssignment.None);

    /// <summary>
    /// Reads each repository's rules file; without a valid one, builds the suggested rules from the listed files (<see cref="AreaFallback"/>).
    /// Reads files: call it off the UI thread.
    /// </summary>
    public static AreaRepoSet Load(IReadOnlyList<FilePath> roots, ProjectListing listing)
    {
        List<FilePath> distinct = [.. roots.Distinct()];
        List<FilePath> deepestFirst = [.. distinct.OrderByDescending(r => r.Value.Length)];
        Dictionary<FilePath, List<string>> relative = distinct.ToDictionary(r => r, _ => new List<string>());
        foreach (ListedFile file in listing.Files)
        {
            if (file.Ghost) continue;
            foreach (FilePath root in deepestFirst)
            {
                if (!file.Path.IsUnder(root)) continue;
                relative[root].Add(file.Path.RelativeTo(root).Replace('\\', '/'));
                break;
            }
        }

        List<AreaRepo> repos = [];
        foreach (FilePath root in distinct)
        {
            string name = root.FileName.Length > 0 ? root.FileName : root.Value;
            AreaRulesRead read = AreaRulesReader.Read(root);
            if (read.Rules is { } rules) repos.Add(new AreaRepo(root, name, rules, new AreaClassifier(root, rules), AreaSource.File, ValueList<AreaRuleError>.Empty));
            else
            {
                AreaRules suggested = AreaFallback.Build(relative[root], name);
                repos.Add(new AreaRepo(root, name, suggested, new AreaClassifier(root, suggested), read.Found ? AreaSource.Invalid : AreaSource.Suggested, read.Errors));
            }
        }
        return new AreaRepoSet(repos);
    }
}
