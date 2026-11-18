using AgentTrace.Core;
using AgentTrace.Core.Areas;
using AgentTrace.Presentation.Areas;
using AgentTrace.Presentation.Resources;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Areas;

public sealed class AreaErrorTextTest
{
    private static AreaRepo Repo(string name, AreaSource source, int errors = 0)
    {
        var rules = new AreaRules(ValueList<AreaRule>.Empty);
        var root = new FilePath("/" + name);
        return new AreaRepo(root, name, rules, new AreaClassifier(root, rules), source,
            Enumerable.Range(0, errors).Select(i => new AreaRuleError(AreaRuleErrorKind.DuplicateId, AreaIndex: i, AreaId: "a" + i)).ToValueList());
    }

    [Fact]
    public void EveryKindOfErrorHasATextWithItsDataInEnglishAndSpanish()
    {
        (AreaRuleError Error, string English, string Spanish)[] cases =
        [
            (new(AreaRuleErrorKind.Unreadable), "The rules file could not be read", "No se pudo leer el archivo de reglas"), // lang-ok: Spanish expectation
            (new(AreaRuleErrorKind.InvalidJson, Line: 1234), "Invalid JSON at line 1,234", "JSON no válido en la línea 1.234"), // lang-ok: Spanish expectation
            (new(AreaRuleErrorKind.InvalidJson), "Invalid JSON", "JSON no válido"), // lang-ok: Spanish expectation
            (new(AreaRuleErrorKind.UnsupportedVersion, Field: "2"), "Unsupported version 2 (only version 1 is supported)", "Versión 2 no admitida (solo se admite la versión 1)"), // lang-ok: Spanish expectation
            (new(AreaRuleErrorKind.MissingField, Field: "version"), "Missing required field \"version\"", "Falta el campo obligatorio \"version\""), // lang-ok: Spanish expectation
            (new(AreaRuleErrorKind.MissingField, AreaIndex: 2, Field: "name"), "area 3: Missing required field \"name\"", "área 3: Falta el campo obligatorio \"name\""), // lang-ok: Spanish expectation
            (new(AreaRuleErrorKind.UnknownDimension, AreaIndex: 0, AreaId: "x", Field: "tier"), "x: Unknown dimension \"tier\" (use layer, module or kind)", "x: Dimensión desconocida \"tier\" (usa layer, module o kind)"), // lang-ok: Spanish expectation
            (new(AreaRuleErrorKind.InvalidId, AreaIndex: 0), "area 1: Invalid id (lowercase letters, digits, - and _, starting with a letter or a digit)", "área 1: Id no válido (minúsculas, dígitos, - y _, empezando por letra o dígito)"), // lang-ok: Spanish expectation
            (new(AreaRuleErrorKind.DuplicateId, AreaIndex: 1, AreaId: "a"), "a: Duplicate id (ids are unique across the whole file)", "a: Id duplicado (los ids son únicos en todo el archivo)"), // lang-ok: Spanish expectation
            (new(AreaRuleErrorKind.EmptyInclude, AreaIndex: 1, AreaId: "a"), "a: \"include\" needs at least one pattern", "a: \"include\" necesita al menos un patrón"), // lang-ok: Spanish expectation
            (new(AreaRuleErrorKind.InvalidGlob, AreaIndex: 1, AreaId: "a", Pattern: "{a,b}", GlobError: GlobError.Braces), "a: Invalid pattern \"{a,b}\": braces are not supported", "a: Patrón no válido \"{a,b}\": las llaves no se admiten"), // lang-ok: Spanish expectation
        ];
        foreach ((AreaRuleError error, string english, _) in cases) Assert.Equal(english, AreaErrorText.Of(error));
        using var spanishCulture = CultureScope.Spanish();
        foreach ((AreaRuleError error, _, string spanish) in cases) Assert.Equal(spanish, AreaErrorText.Of(error));
    }

    [Fact]
    public void EveryGlobErrorHasADistinctText()
    {
        GlobError[] all = Enum.GetValues<GlobError>();
        string[] texts = [.. all.Select(AreaErrorText.Glob)];
        Assert.All(texts, t => Assert.NotEmpty(t));
        Assert.Equal(all.Length, texts.Distinct().Count());
    }

    [Fact]
    public void TheErrorLinesAreCappedWithAMoreLineAndNamedByRepositoryWhenThereAreSeveral()
    {
        IReadOnlyList<string> one = AreaText.ErrorLines(new AreaRepoSet([Repo("api", AreaSource.Invalid, 7)]), 5);
        Assert.Equal(6, one.Count);
        Assert.Equal("a0: Duplicate id (ids are unique across the whole file)", one[0]);
        Assert.Equal("+2 more errors", one[5]);

        IReadOnlyList<string> two = AreaText.ErrorLines(new AreaRepoSet([Repo("api", AreaSource.Invalid, 1), Repo("web", AreaSource.Invalid, 1)]), 5);
        Assert.Equal(["api: a0: Duplicate id (ids are unique across the whole file)", "web: a0: Duplicate id (ids are unique across the whole file)"], two);
        Assert.Equal("+1 more error", AreaText.ErrorLines(new AreaRepoSet([Repo("api", AreaSource.Invalid, 3)]), 2)[2]);
    }

    [Fact]
    public void TheSourceAndTheCoverageReadAsOneLineEachInBothLanguages()
    {
        var coverage = new AreaCoverage(new DimensionCoverage[] { new(AreaDimension.Layer, 1, 2), new(AreaDimension.Module, 2, 2) }.ToValueList(), false);

        Assert.Equal("Rules: .agenttrace/areas.json", AreaText.Source(new AreaRepoSet([Repo("a", AreaSource.File)])));
        Assert.Equal("Rules: suggested (no .agenttrace/areas.json)", AreaText.Source(new AreaRepoSet([Repo("a", AreaSource.Suggested)])));
        Assert.Equal("Rules: suggested (.agenttrace/areas.json has errors)", AreaText.Source(new AreaRepoSet([Repo("a", AreaSource.Invalid), Repo("b", AreaSource.Suggested)])));
        Assert.Equal("Rules: .agenttrace/areas.json in 1 of 2 repositories, suggested in the rest", AreaText.Source(new AreaRepoSet([Repo("a", AreaSource.File), Repo("b", AreaSource.Suggested)])));
        Assert.Equal("Layer 50% · Module 100%", AreaText.CoverageItems(coverage));
        using var spanishCulture = CultureScope.Spanish();
        Assert.Equal("Capa 50 % · Módulo 100 %", AreaText.CoverageItems(coverage)); // lang-ok: Spanish expectation
    }

    [Fact]
    public void TheSetupPromptKeepsItsJsonBracesAndExplainsTheFormatInBothLanguages()
    {
        string english = AreaStrings.SetupPrompt;
        Assert.Contains("{ \"version\": 1, \"areas\": [", english, StringComparison.Ordinal);
        Assert.Contains(".agenttrace/areas.json", english, StringComparison.Ordinal);
        Assert.Contains("90 %", english, StringComparison.Ordinal);
        Assert.Contains("git ls-files", english, StringComparison.Ordinal);
        using var spanishCulture = CultureScope.Spanish();
        string spanish = AreaStrings.SetupPrompt;
        Assert.NotEqual(english, spanish);
        Assert.Contains("{ \"version\": 1, \"areas\": [", spanish, StringComparison.Ordinal);
        Assert.Contains("90 %", spanish, StringComparison.Ordinal);
    }
}
