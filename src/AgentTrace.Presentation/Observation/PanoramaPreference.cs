using System.Text;
using AgentTrace.Core;

namespace AgentTrace.Presentation.Observation;

/// <summary>
/// The Panorama layout the user left, in a local text file (<c>key=value</c> lines): nothing leaves the machine.
/// Reading never fails: a missing, unreadable or corrupt file means the default layout.
/// </summary>
public sealed class PanoramaPreference(FilePath file)
{
    /// <summary>The standard file: <c>~/.agenttrace-studio/panorama.txt</c>.</summary>
    public static PanoramaPreference Standard() => new(new FilePath(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agenttrace-studio", "panorama.txt")));

    public PanoramaLayout Load()
    {
        try
        {
            return File.Exists(file.Value) ? PanoramaLayout.Parse(File.ReadAllLines(file.Value, Encoding.UTF8)) : PanoramaLayout.Default;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return PanoramaLayout.Default;
        }
    }

    /// <summary>Saves the layout. Does not throw: the preference is a convenience, not data.</summary>
    public void Save(PanoramaLayout layout)
    {
        try
        {
            Directory.CreateDirectory(file.Parent!.Value.Value);
            File.WriteAllText(file.Value, layout.Normalized().Format(), new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // cannot write: the layout only lasts until the application closes
        }
    }
}
