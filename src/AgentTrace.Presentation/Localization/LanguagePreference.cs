using System.Text;
using AgentTrace.Core;

namespace AgentTrace.Presentation.Localization;

/// <summary>
/// The language the user chose, in a local text file (one line, the language code): nothing leaves the machine.
/// Reading never fails: a missing, unreadable or unsupported value means "no preference".
/// </summary>
public sealed class LanguagePreference(FilePath file)
{
    /// <summary>The standard file: <c>~/.agenttrace-studio/language.txt</c>.</summary>
    public static LanguagePreference Standard() => new(new FilePath(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agenttrace-studio", "language.txt")));

    /// <summary>The saved language code, or null if there is none or it is not supported.</summary>
    public string? Load()
    {
        try
        {
            if (!File.Exists(file.Value)) return null;
            string? line = File.ReadLines(file.Value, Encoding.UTF8).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            return SupportedLanguages.Find(line)?.Code;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Saves the language. Does not throw: the preference is a convenience, not data.</summary>
    public void Save(Language language)
    {
        try
        {
            Directory.CreateDirectory(file.Parent!.Value.Value);
            File.WriteAllText(file.Value, language.Code + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // cannot write: the choice only lasts until the application restarts
        }
    }
}
