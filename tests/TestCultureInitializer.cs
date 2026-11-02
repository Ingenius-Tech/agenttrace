using System.Globalization;
using System.Runtime.CompilerServices;

namespace AgentTrace.Tests;

// Shared by every *.Tests project (see tests/Directory.Build.props): tests run in English by default, whatever the
// language of the machine, so that texts and number formats are deterministic. The default culture (and not only the
// current thread's) is set because tests also run code on other threads (the Avalonia headless UI thread).
// A test that checks another language uses CultureScope (AgentTrace.Testing).
// xUnit v3's "culture" option in xunit.runner.json was tried first: it behaves the same, but needs one more file per project.
internal static class TestCultureInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var english = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = english;
        CultureInfo.DefaultThreadCurrentUICulture = english;
        CultureInfo.CurrentCulture = english;
        CultureInfo.CurrentUICulture = english;
    }
}
