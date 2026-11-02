using System.Globalization;

namespace AgentTrace.Testing;

/// <summary>
/// Runs a block of a test in another culture and restores the previous one on dispose
/// (<c>using var _ = CultureScope.Spanish();</c>). It sets the culture of the current thread only: code that needs it on
/// another thread (for example the Avalonia UI thread) must create the scope on that thread.
/// </summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _previousUiCulture = CultureInfo.CurrentUICulture;

    public CultureScope(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static CultureScope Spanish() => new("es-ES");

    public static CultureScope English() => new("en-US");

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previousCulture;
        CultureInfo.CurrentUICulture = _previousUiCulture;
    }
}
