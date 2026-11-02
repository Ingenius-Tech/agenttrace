using System.Text;
using AgentTrace.Core;

namespace AgentTrace.Presentation.Observation;

/// <summary>
/// The last projects opened, from the most recent to the oldest. They are stored in a local text file (one path per
/// line): nothing leaves the machine. What no longer exists is discarded when reading.
/// </summary>
public sealed class RecentProjects(FilePath file)
{
    public const int Max = 8;

    /// <summary>The standard file: <c>~/.agenttrace-studio/recent-projects.txt</c>.</summary>
    public static RecentProjects Standard() => new(new FilePath(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agenttrace-studio", "recent-projects.txt")));

    public IReadOnlyList<FilePath> List()
    {
        List<FilePath> output = [];
        try
        {
            if (!File.Exists(file.Value)) return output;
            foreach (string line in File.ReadAllLines(file.Value, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var p = new FilePath(line.Trim());
                    if (Directory.Exists(p.Value) && !output.Contains(p)) output.Add(p);
                }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // a line that is no longer a valid path is ignored
                }
                if (output.Count == Max) break;
            }
        }
        catch (IOException)
        {
            // with no readable history, the list is empty
        }
        return output;
    }

    public FilePath? Last() => List() is [var first, ..] ? first : null;

    /// <summary>Puts the project first (without duplicates) and saves it. Does not throw: if it cannot be written, the history is simply lost.</summary>
    public void Add(FilePath project)
    {
        List<FilePath> all = [.. List()];
        all.Remove(project);
        all.Insert(0, project);
        try
        {
            Directory.CreateDirectory(file.Parent!.Value.Value);
            File.WriteAllLines(file.Value, all.Take(Max).Select(p => p.Value), new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the history is a convenience, not data: the user is not bothered
        }
    }
}
