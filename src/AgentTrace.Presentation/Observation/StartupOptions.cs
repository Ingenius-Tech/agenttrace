using System.Globalization;
using AgentTrace.Core;
using AgentTrace.Presentation.Localization;
using AgentTrace.Presentation.Resources;

namespace AgentTrace.Presentation.Observation;

/// <summary>
/// Command line arguments: the project to open and the options to check the UI without using the mouse.
/// <c>&lt;project&gt;</c> (the first unnamed argument), <c>--open</c>, <c>--diff</c>, <c>--tab</c>, <c>--kg-select</c>,
/// <c>--kg-view</c> (<c>2d</c>, <c>3d</c>, <c>top</c>, <c>side</c>), <c>--kg-yaw</c>, <c>--kg-pitch</c> (degrees), <c>--search</c> (opens the search window with that text),
/// <c>--graph-select</c>, <c>--graph-collapse</c>, <c>--graph-orientation</c>, <c>--graph-filter</c>, <c>--graph-zoom</c>,
/// <c>--heatmap-zoom</c> (folder to zoom into), <c>--heatmap-filter</c> (<c>reads,writes,errors</c>), <c>--heatmap-area</c> (<c>size</c> or <c>activity</c>),
/// <c>--heatmap-select</c> (file or folder to select), <c>--lang</c> (<c>en</c> or <c>es</c>: language of this run) and, to
/// capture the window in automatic checks, <c>--capture</c>, <c>--capture-delay</c> and <c>--render=software</c>.
/// What is not understood is recorded in <see cref="Warnings"/> and does not prevent startup.
/// </summary>
public sealed record StartupOptions(
    FilePath? Project,
    string? Open,
    string? Diff,
    string? Tab,
    string? KnowledgeSelect,
    string? GraphSelect,
    string? GraphCollapse,
    string? GraphOrientation,
    string? GraphFilter,
    double? GraphZoom,
    string? KnowledgeView,
    double? KnowledgeYaw,
    double? KnowledgePitch,
    string? Search,
    string? HeatmapZoom,
    string? HeatmapFilter,
    string? HeatmapArea,
    string? HeatmapSelect,
    string? Lang,
    string? Capture,
    int CaptureDelayMillis,
    bool SoftwareRendering,
    bool PrintWindowCapture,
    IReadOnlyList<string> Warnings)
{
    public const int DefaultCaptureDelayMillis = 1500;

    private static readonly HashSet<string> KnowledgeViews = ["2d", "3d", "top", "side"];

    private static readonly HashSet<string> Tabs = ["graph", "diff", "telemetry", "file", "knowledge", "heatmap"];

    private static readonly HashSet<string> HeatmapAreas = ["size", "activity"];

    public static StartupOptions Parse(IEnumerable<string> args)
    {
        Dictionary<string, string> named = [];
        List<string> unnamed = [];
        List<string> warnings = [];
        foreach (string arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                unnamed.Add(arg);
                continue;
            }
            int eq = arg.IndexOf('=');
            string key = eq < 0 ? arg[2..] : arg[2..eq];
            named[key] = eq < 0 ? "" : arg[(eq + 1)..];
        }

        FilePath? project = null;
        if (unnamed.Count > 0 && Directory.Exists(unnamed[0])) project = new FilePath(unnamed[0]);

        string? tab = Text(named, "tab")?.ToLowerInvariant();
        if (tab is not null && !Tabs.Contains(tab))
        {
            warnings.Add(string.Format(CultureInfo.CurrentCulture, ShellStrings.StartupWarningUnknownTabFormat, tab));
            tab = null;
        }

        double? zoom = null;
        if (Text(named, "graph-zoom") is { } zoomText)
        {
            if (double.TryParse(zoomText, NumberStyles.Float, CultureInfo.InvariantCulture, out double z)) zoom = z;
            else warnings.Add(string.Format(CultureInfo.CurrentCulture, ShellStrings.StartupWarningNotANumberFormat, "graph-zoom", zoomText));
        }

        string? kgView = Text(named, "kg-view")?.ToLowerInvariant();
        if (kgView is not null && !KnowledgeViews.Contains(kgView))
        {
            warnings.Add(string.Format(CultureInfo.CurrentCulture, ShellStrings.StartupWarningUnknownKgViewFormat, kgView));
            kgView = null;
        }
        double? yaw = Degrees(named, "kg-yaw", warnings);
        double? pitch = Degrees(named, "kg-pitch", warnings);

        string? heatmapArea = Text(named, "heatmap-area")?.ToLowerInvariant();
        if (heatmapArea is not null && !HeatmapAreas.Contains(heatmapArea))
        {
            warnings.Add(string.Format(CultureInfo.CurrentCulture, ShellStrings.StartupWarningUnknownHeatmapAreaFormat, heatmapArea));
            heatmapArea = null;
        }

        string? lang = null;
        if (Text(named, "lang") is { } langText)
        {
            if (SupportedLanguages.Find(langText) is { } language) lang = language.Code;
            else
            {
                warnings.Add(string.Format(CultureInfo.CurrentCulture, ShellStrings.StartupWarningUnknownLangFormat,
                    langText, string.Join(", ", SupportedLanguages.All.Select(l => l.Code))));
            }
        }

        int delay = DefaultCaptureDelayMillis;
        if (Text(named, "capture-delay") is { } delayText)
        {
            if (int.TryParse(delayText, NumberStyles.None, CultureInfo.InvariantCulture, out int d)) delay = d;
            else warnings.Add(string.Format(CultureInfo.CurrentCulture, ShellStrings.StartupWarningNotANumberFormat, "capture-delay", delayText));
        }

        bool software = false;
        if (named.TryGetValue("render", out string? render))
        {
            if (render == "software") software = true;
            else warnings.Add(string.Format(CultureInfo.CurrentCulture, ShellStrings.StartupWarningUnknownRenderFormat, render));
        }

        return new StartupOptions(
            project,
            Text(named, "open"),
            Text(named, "diff"),
            tab,
            Text(named, "kg-select"),
            Text(named, "graph-select"),
            Text(named, "graph-collapse"),
            Text(named, "graph-orientation"),
            Text(named, "graph-filter"),
            zoom,
            kgView,
            yaw,
            pitch,
            Text(named, "search"),
            Text(named, "heatmap-zoom"),
            Text(named, "heatmap-filter"),
            heatmapArea,
            Text(named, "heatmap-select"),
            lang,
            Text(named, "capture"),
            delay,
            software,
            Text(named, "capture-mode")?.Equals("printwindow", StringComparison.OrdinalIgnoreCase) == true,
            warnings);
    }

    private static double? Degrees(Dictionary<string, string> named, string key, List<string> warnings)
    {
        if (Text(named, key) is not { } text) return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return value;
        warnings.Add(string.Format(CultureInfo.CurrentCulture, ShellStrings.StartupWarningNotANumberFormat, key, text));
        return null;
    }

    private static string? Text(Dictionary<string, string> named, string key) =>
        named.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;

}
