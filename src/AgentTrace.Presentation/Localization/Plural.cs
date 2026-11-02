namespace AgentTrace.Presentation.Localization;

/// <summary>
/// Picks the singular or plural format of a counted text, e.g.
/// <c>string.Format(culture, Plural.Pick(n, S.StepCount_One, S.StepCount_Other), LocalNumbers.Grouped(n))</c>.
/// One/other is right for English and Spanish; a language with more plural forms needs a richer rule.
/// </summary>
public static class Plural
{
    public static string Pick(long n, string one, string other) => n == 1 ? one : other;
}
