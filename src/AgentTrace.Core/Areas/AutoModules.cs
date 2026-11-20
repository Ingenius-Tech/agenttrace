namespace AgentTrace.Core.Areas;

/// <summary>
/// Modules inferred without a rules file: each folder holding a build manifest (<c>*.csproj</c>, <c>package.json</c>...) is a module,
/// and the files under no such folder belong to a root module. Which names are manifests is data (see <c>ManifestCatalog</c>), so the core
/// knows no language.
/// </summary>
public static class AutoModules
{
    /// <summary>Id of the module that holds the files under no manifest folder.</summary>
    public const string RootId = ".";

    /// <summary>
    /// Module areas ordered so that the deepest folder wins (first match). Module id is the folder relative to the root, name the folder
    /// name; the root module is named <paramref name="rootName"/>. Folders whose path cannot be written as a glob (or holds a wildcard character) are not modules.
    /// </summary>
    /// <param name="relativePaths">listed files, relative to the repository root, <c>/</c>-separated</param>
    /// <param name="manifestPatterns">globs of manifest file names, matched against the file name only</param>
    public static AreaRules Build(IEnumerable<string> relativePaths, IEnumerable<string> manifestPatterns, string rootName)
    {
        GlobPattern[] manifests = manifestPatterns.Select(p => GlobPattern.TryCreate(p, out _)).OfType<GlobPattern>().ToArray();
        SortedSet<string> folders = new(StringComparer.Ordinal);
        foreach (string path in relativePaths)
        {
            int slash = path.LastIndexOf('/');
            string name = path[(slash + 1)..];
            if (slash > 0 && manifests.Any(m => m.IsMatch(name))) folders.Add(path[..slash]);
        }
        List<AreaRule> modules = [];
        foreach (string folder in folders.OrderByDescending(f => f.Count(c => c == '/')).ThenBy(f => f, StringComparer.Ordinal))
        {
            // The glob has no escape: a folder named with * or ? would become a wildcard and capture its siblings
            if (folder.AsSpan().IndexOfAny('*', '?') >= 0) continue;
            string glob = folder + "/**";
            if (GlobPattern.Validate(glob) is not null) continue;
            modules.Add(new AreaRule(folder, folder[(folder.LastIndexOf('/') + 1)..], AreaDimension.Module, [glob], []));
        }
        modules.Add(new AreaRule(RootId, rootName, AreaDimension.Module, ["**"], []));
        return new AreaRules(modules.ToValueList());
    }
}
