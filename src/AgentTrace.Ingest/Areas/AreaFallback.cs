using System.Reflection;
using System.Text.Json;
using AgentTrace.Core.Areas;

namespace AgentTrace.Ingest.Areas;

/// <summary>
/// What classifies a repository that has no valid rules file, so the Areas map is never empty: modules inferred from the manifests found
/// in the listed files (<see cref="ManifestCatalog"/>) plus the generic preset of kinds. There are no layers: layer stays unclassified.
/// </summary>
public static class AreaFallback
{
    /// <param name="relativePaths">listed files, relative to the repository root, <c>/</c>-separated</param>
    /// <param name="rootName">name of the root module (the repository folder name)</param>
    public static AreaRules Build(IEnumerable<string> relativePaths, string rootName) =>
        AutoModules.Build(relativePaths, ManifestCatalog.Patterns, rootName).Then(ManifestCatalog.GenericPreset);
}

/// <summary>Embedded data: the manifest file names that mark a module, and the generic preset of kinds (same format as <c>areas.json</c>).</summary>
public static class ManifestCatalog
{
    private static readonly Lazy<IReadOnlyList<string>> PatternList = new(LoadPatterns);
    private static readonly Lazy<AreaRules> Preset = new(LoadPreset);

    /// <summary>Globs of manifest file names (<c>*.csproj</c>, <c>package.json</c>...).</summary>
    public static IReadOnlyList<string> Patterns => PatternList.Value;

    /// <summary>The generic preset: kinds only (test, docs, migration, contract, dependencies, config), no layers.</summary>
    public static AreaRules GenericPreset => Preset.Value;

    private static string Resource(string name)
    {
        Assembly assembly = typeof(ManifestCatalog).Assembly;
        using Stream stream = assembly.GetManifestResourceStream("AgentTrace.Ingest.Areas." + name)
            ?? throw new InvalidOperationException("Missing embedded resource " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IReadOnlyList<string> LoadPatterns()
    {
        using JsonDocument document = JsonDocument.Parse(Resource("manifests.json"));
        return document.RootElement.GetProperty("manifests").EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    private static AreaRules LoadPreset() =>
        AreaRulesReader.Parse(Resource("generic-preset.json")).Rules ?? throw new InvalidOperationException("The embedded generic preset is invalid");
}
