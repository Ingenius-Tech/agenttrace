using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AgentTrace.Testing;

namespace AgentTrace.Core.Tests.Repository;

/// <summary>
/// The repository is written in English: this test fails on Spanish diacritics or on a few unambiguous Spanish words in tracked
/// text files. Allowed: <c>*.es.resx</c> (the Spanish translations), <c>tests/**/TestData/**</c>, the plan that quotes the Spanish
/// originals and any line carrying the marker <c>lang-ok</c> (deliberate non-English data, for example accent folding tests).
/// </summary>
public sealed partial class LanguageLintTest
{
    private const string Marker = "lang-ok";

    private const int MaxReported = 50;

    private static readonly string[] Extensions =
        [".cs", ".md", ".yml", ".yaml", ".props", ".csproj", ".axaml", ".slnx", ".json", ".resx", ".xml", ".txt"];

    // Diacritics and inverted marks, or a Spanish word standing alone (not an XML tag such as <para> nor part of an identifier)
    [GeneratedRegex("[áéíóúñÁÉÍÓÚÑ¿¡]|(?<![</\\w])(el|los|las|del|para|una|que|archivo|archivos|carpeta|sesión)(?![>\\w])")] // lang-ok
    private static partial Regex Spanish();

    /// <summary>The offending lines of a text as <c>line: text</c>, skipping the lines with the marker.</summary>
    private static IEnumerable<string> Offenders(string text)
    {
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (!line.Contains(Marker, StringComparison.Ordinal) && Spanish().IsMatch(line))
            {
                yield return (i + 1) + ": " + line.Trim();
            }
        }
    }

    private static bool IsAllowed(string path) =>
        path.EndsWith(".es.resx", StringComparison.OrdinalIgnoreCase)
        || (path.StartsWith("tests/", StringComparison.Ordinal) && path.Contains("/TestData/", StringComparison.Ordinal));

    private static List<string>? TrackedFiles(string root)
    {
        try
        {
            var info = new ProcessStartInfo("git", ["-C", root, "ls-files", "-z"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using Process process = Process.Start(info)!;
            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries)] : null;
        }
        catch (Exception e) when (e is IOException or Win32Exception)
        {
            return null;
        }
    }

    [Fact]
    public void TheTrackedTextFilesAreInEnglish()
    {
        string root = TestPaths.RepoRoot().Value;
        Assert.SkipUnless(Directory.Exists(Path.Combine(root, "src")), "The repository root was not found");
        List<string>? files = TrackedFiles(root);
        Assert.SkipUnless(files is not null, "This test needs git and a git repository");

        List<string> found = [];
        int total = 0;
        foreach (string path in files!.Where(p => Extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase) && !IsAllowed(p)))
        {
            string full = Path.Combine(root, path);
            if (!File.Exists(full)) continue; // tracked but deleted in the working tree
            foreach (string offender in Offenders(File.ReadAllText(full)))
            {
                total++;
                if (found.Count < MaxReported) found.Add(path + ":" + offender);
            }
        }

        Assert.True(total == 0,
            "Spanish text found in " + total + " lines. Translate it, or mark a deliberate line with '" + Marker + "':\n" + string.Join('\n', found));
    }

    [Fact]
    public void TheScannerFlagsSpanishAndIgnoresTheMarkerAndXmlTags()
    {
        Assert.Equal(["1: // Comprueba el archivo"], Offenders("// Comprueba el archivo")); // lang-ok
        Assert.Equal(["1: var s = \"niño\";"], Offenders("var s = \"niño\";")); // lang-ok
        Assert.Empty(Offenders("var s = \"niño\"; // lang-ok")); // lang-ok
        Assert.Empty(Offenders("/// <para>Plain English</para>"));
        Assert.Empty(Offenders("var el1 = 2; // identifiers are fine"));
    }
}
