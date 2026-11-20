using System.Globalization;
using System.Text.RegularExpressions;

namespace AgentTrace.Core.Areas;

/// <summary>What an area classifies: an architectural layer, a module or service, or the kind of change (test, docs, config...).</summary>
public enum AreaDimension { Layer, Module, Kind }

/// <summary>
/// An area: the files matched by <c>Include</c> and not by <c>Exclude</c>. <c>Name</c> is user data, shown as is. <c>Shared</c>: a module
/// other modules depend on (only meaningful for modules). Kind ids <c>test</c> and <c>contract</c> have a documented meaning.
/// </summary>
public sealed record AreaRule(string Id, string Name, AreaDimension Dimension, ValueList<string> Include, ValueList<string> Exclude, bool Shared = false);

/// <summary>Kind of problem found in a rules file. The presentation layer turns each into text.</summary>
public enum AreaRuleErrorKind
{
    Unreadable,
    InvalidJson,
    UnsupportedVersion,
    MissingField,
    UnknownDimension,
    InvalidId,
    DuplicateId,
    EmptyInclude,
    InvalidGlob,
}

/// <summary>
/// A problem in a rules file, as data. <c>AreaIndex</c>: position of the area in the file (0-based). <c>Pattern</c> and
/// <c>GlobError</c>: the rejected glob. <c>Line</c>: 1-based line of an <c>InvalidJson</c>. <c>Field</c>: the missing field of a
/// <c>MissingField</c>, or the offending value of an <c>UnknownDimension</c> or <c>UnsupportedVersion</c>.
/// </summary>
public sealed record AreaRuleError(AreaRuleErrorKind Kind, int? AreaIndex = null, string? AreaId = null, string? Pattern = null,
    GlobError? GlobError = null, long? Line = null, string? Field = null);

/// <summary>An area as read from a file, before validation: any field may be missing.</summary>
public sealed record RawAreaRule(string? Id, string? Name, string? Dimension, IReadOnlyList<string>? Include, IReadOnlyList<string>? Exclude, bool Shared);

/// <summary>The outcome of validating a rules file: the rules, or every error found (never both).</summary>
public sealed record AreaRulesResult(AreaRules? Rules, ValueList<AreaRuleError> Errors)
{
    public bool IsValid => Rules is not null;
}

/// <summary>
/// An ordered set of area rules (version 1 of the format). Within each dimension the first area, in file order, whose
/// <c>Include</c> matches and whose <c>Exclude</c> does not, wins. A file with any error is never applied partially.
/// </summary>
public sealed record AreaRules(ValueList<AreaRule> Areas)
{
    public const int SupportedVersion = 1;

    private static readonly Regex IdPattern = new(@"\A[a-z0-9][a-z0-9_-]*\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static readonly AreaRules Empty = new(ValueList<AreaRule>.Empty);

    /// <summary>The rules followed by those of <paramref name="other"/> (which therefore only win where these do not match).</summary>
    public AreaRules Then(AreaRules other) => new(Areas.Concat(other.Areas).ToValueList());

    /// <summary>
    /// Validates a parsed file. <paramref name="version"/> and <paramref name="areas"/> are null when absent. Collects every error;
    /// with any, there are no rules.
    /// </summary>
    public static AreaRulesResult Validate(int? version, IReadOnlyList<RawAreaRule>? areas)
    {
        List<AreaRuleError> errors = [];
        if (version is null) errors.Add(new AreaRuleError(AreaRuleErrorKind.MissingField, Field: "version"));
        else if (version != SupportedVersion)
        {
            errors.Add(new AreaRuleError(AreaRuleErrorKind.UnsupportedVersion, Field: version.Value.ToString(CultureInfo.InvariantCulture)));
        }
        if (areas is null) errors.Add(new AreaRuleError(AreaRuleErrorKind.MissingField, Field: "areas"));

        List<AreaRule> rules = [];
        HashSet<string> ids = [];
        for (int i = 0; areas is not null && i < areas.Count; i++)
        {
            RawAreaRule raw = areas[i];
            int before = errors.Count;
            int index = i;
            void Add(AreaRuleErrorKind kind, string? pattern = null, GlobError? glob = null, string? field = null) =>
                errors.Add(new AreaRuleError(kind, index, raw.Id, pattern, glob, Field: field));

            if (string.IsNullOrEmpty(raw.Id)) Add(AreaRuleErrorKind.MissingField, field: "id");
            else if (!IdPattern.IsMatch(raw.Id)) Add(AreaRuleErrorKind.InvalidId);
            else if (!ids.Add(raw.Id)) Add(AreaRuleErrorKind.DuplicateId);
            if (string.IsNullOrWhiteSpace(raw.Name)) Add(AreaRuleErrorKind.MissingField, field: "name");
            AreaDimension? dimension = ParseDimension(raw.Dimension);
            if (raw.Dimension is null) Add(AreaRuleErrorKind.MissingField, field: "dimension");
            else if (dimension is null) Add(AreaRuleErrorKind.UnknownDimension, field: raw.Dimension);
            if (raw.Include is null) Add(AreaRuleErrorKind.MissingField, field: "include");
            else if (raw.Include.Count == 0) Add(AreaRuleErrorKind.EmptyInclude);
            foreach (string pattern in (raw.Include ?? []).Concat(raw.Exclude ?? []))
            {
                if (GlobPattern.Validate(pattern) is { } glob) Add(AreaRuleErrorKind.InvalidGlob, pattern, glob);
            }

            if (errors.Count == before && dimension is { } d)
            {
                rules.Add(new AreaRule(raw.Id!, raw.Name!, d, (raw.Include ?? []).ToValueList(), (raw.Exclude ?? []).ToValueList(), raw.Shared));
            }
        }
        return errors.Count > 0
            ? new AreaRulesResult(null, errors.ToValueList())
            : new AreaRulesResult(new AreaRules(rules.ToValueList()), ValueList<AreaRuleError>.Empty);
    }

    private static AreaDimension? ParseDimension(string? text) => text switch
    {
        "layer" => AreaDimension.Layer,
        "module" => AreaDimension.Module,
        "kind" => AreaDimension.Kind,
        _ => null,
    };
}
