using System.Text.Json;
using AgentTrace.Core;
using AgentTrace.Core.Areas;

namespace AgentTrace.Ingest.Areas;

/// <summary>
/// What reading a rules file gave. <c>Found</c>: the file exists. <c>Rules</c> is null if it does not exist or has errors; a file
/// with errors is never applied partially.
/// </summary>
public sealed record AreaRulesRead(bool Found, AreaRules? Rules, ValueList<AreaRuleError> Errors)
{
    public static readonly AreaRulesRead NotFound = new(false, null, ValueList<AreaRuleError>.Empty);
}

/// <summary>A cheap fingerprint of a rules file (does it exist, size, last write): equal fingerprints mean the file need not be read again.</summary>
public readonly record struct AreaRulesSignature(bool Exists, long Length, DateTime LastWriteUtc);

/// <summary>
/// Reads <c>.agenttrace/areas.json</c> of a repository. It opens that explicit path, so the rule that dot-paths are not tracked
/// (<see cref="ProjectPaths.IsHidden"/>) does not affect it. Never throws: problems come back as <see cref="AreaRuleError"/>.
/// </summary>
public static class AreaRulesReader
{
    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false };

    /// <summary>Location of the rules file of the repository at <paramref name="repoRoot"/>.</summary>
    public static FilePath FileOf(FilePath repoRoot) => repoRoot.Combine(".agenttrace", "areas.json");

    /// <summary>The fingerprint of the rules file of the repository at <paramref name="repoRoot"/>; a file that cannot be inspected counts as missing.</summary>
    public static AreaRulesSignature SignatureOf(FilePath repoRoot)
    {
        try
        {
            var info = new FileInfo(FileOf(repoRoot).Value);
            return info.Exists ? new AreaRulesSignature(true, info.Length, info.LastWriteTimeUtc) : default;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    public static AreaRulesRead Read(FilePath repoRoot)
    {
        FilePath file = FileOf(repoRoot);
        string text;
        try
        {
            if (!File.Exists(file.Value)) return AreaRulesRead.NotFound;
            text = File.ReadAllText(file.Value);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new AreaRulesRead(true, null, [new AreaRuleError(AreaRuleErrorKind.Unreadable)]);
        }
        return Parse(text);
    }

    /// <summary>Parses and validates the text of a rules file (also used for the embedded preset).</summary>
    public static AreaRulesRead Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, Options);
        }
        catch (JsonException e)
        {
            return Invalid(new AreaRuleError(AreaRuleErrorKind.InvalidJson, Line: (e.LineNumber ?? -1) + 1));
        }
        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Invalid(new AreaRuleError(AreaRuleErrorKind.InvalidJson));
            int? version = root.TryGetProperty("version", out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : null;
            List<RawAreaRule>? areas = null;
            if (root.TryGetProperty("areas", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                areas = list.EnumerateArray().Select(RawOf).ToList();
            }
            AreaRulesResult result = AreaRules.Validate(version, areas);
            return new AreaRulesRead(true, result.Rules, result.Errors);
        }
    }

    // A property of the wrong type counts as absent.
    private static RawAreaRule RawOf(JsonElement area) => new(
        Text(area, "id"), Text(area, "name"), Text(area, "dimension"), Texts(area, "include"), Texts(area, "exclude"),
        area.ValueKind == JsonValueKind.Object && area.TryGetProperty("shared", out JsonElement shared) && shared.ValueKind == JsonValueKind.True);

    private static string? Text(JsonElement area, string name) =>
        area.ValueKind == JsonValueKind.Object && area.TryGetProperty(name, out JsonElement p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static List<string>? Texts(JsonElement area, string name)
    {
        if (area.ValueKind != JsonValueKind.Object || !area.TryGetProperty(name, out JsonElement p) || p.ValueKind != JsonValueKind.Array) return null;
        return p.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList();
    }

    private static AreaRulesRead Invalid(AreaRuleError error) => new(true, null, [error]);
}
