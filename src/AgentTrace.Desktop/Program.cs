using System.Globalization;
using Avalonia;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Observation;

namespace AgentTrace.Desktop;

internal static class Program
{
    /// <summary>The command line arguments of this run (set in <c>Main</c>).</summary>
    public static StartupOptions Options { get; private set; } = StartupOptions.Parse([]);

    /// <summary>The UI language of this run (set in <c>Main</c>, before Avalonia starts).</summary>
    public static Language Language { get; private set; } = SupportedLanguages.English;

    [STAThread]
    public static int Main(string[] args)
    {
        // --lang > saved preference > OS language > English; the cultures are fixed before any text is produced
        CultureInfo osUi = CultureInfo.CurrentUICulture;
        CultureInfo osFormats = CultureInfo.CurrentCulture;
        Language = LanguageResolver.Resolve(StartupOptions.Parse(args).Lang, LanguagePreference.Standard().Load(), osUi);
        LanguageResolver.Apply(Language, osFormats);
        // parsed again so that the warnings are written in the chosen language
        Options = StartupOptions.Parse(args);
        foreach (string warning in Options.Warnings) Console.Error.WriteLine(warning);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the headless tests and the designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder builder = AppBuilder.Configure<App>().UsePlatformDetect();
        if (Options.SoftwareRendering && OperatingSystem.IsWindows())
        {
            // Equivalent to -Dprism.order=sw of the Java version: allows capturing with PrintWindow.
            builder = builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] });
        }
        return builder;
    }
}
