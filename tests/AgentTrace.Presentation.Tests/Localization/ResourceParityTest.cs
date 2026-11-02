using System.Text.RegularExpressions;
using System.Xml.Linq;
using AgentTrace.Testing;

namespace AgentTrace.Presentation.Tests.Localization;

/// <summary>Every resource file has its Spanish twin with the same keys and the same placeholders, and nothing is empty.</summary>
public sealed partial class ResourceParityTest
{
    private static readonly string ResourcesDir =
        Path.Combine(TestPaths.RepoRoot().Value, "src", "AgentTrace.Presentation", "Resources");

    // {0} and {0:N1}
    [GeneratedRegex(@"\{\d+(?::[^}]*)?\}")]
    private static partial Regex Placeholder();

    private static Dictionary<string, string> Read(string file) =>
        XDocument.Load(file).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    private static string[] NeutralFiles() =>
        [.. Directory.GetFiles(ResourcesDir, "*.resx").Where(f => Path.GetFileNameWithoutExtension(f).Count(c => c == '.') == 0).Order()];

    [Fact]
    public void TheFiveAreasExist()
    {
        Assert.Equal(
            ["CommonStrings", "GraphStrings", "MapStrings", "ShellStrings", "TextStrings"],
            NeutralFiles().Select(Path.GetFileNameWithoutExtension));
    }

    [Fact]
    public void EveryNeutralFileHasASpanishTwinAndViceVersa()
    {
        foreach (string neutral in NeutralFiles())
        {
            string twin = Path.ChangeExtension(neutral, ".es.resx");
            Assert.True(File.Exists(twin), "missing " + twin);
        }
        foreach (string spanish in Directory.GetFiles(ResourcesDir, "*.es.resx"))
        {
            string neutral = spanish[..^".es.resx".Length] + ".resx";
            Assert.True(File.Exists(neutral), "the Spanish file has no neutral one: " + spanish);
        }
    }

    [Fact]
    public void KeySetsAreIdentical()
    {
        foreach (string neutral in NeutralFiles())
        {
            var en = Read(neutral).Keys.Order().ToList();
            var es = Read(Path.ChangeExtension(neutral, ".es.resx")).Keys.Order().ToList();
            Assert.True(en.SequenceEqual(es),
                Path.GetFileName(neutral) + ": only in en [" + string.Join(", ", en.Except(es)) + "], only in es [" + string.Join(", ", es.Except(en)) + "]");
        }
    }

    [Fact]
    public void PlaceholderSetsAreIdentical()
    {
        foreach (string neutral in NeutralFiles())
        {
            var en = Read(neutral);
            var es = Read(Path.ChangeExtension(neutral, ".es.resx"));
            foreach ((string key, string value) in en)
            {
                if (!es.TryGetValue(key, out string? other)) continue; // reported by KeySetsAreIdentical
                var a = Placeholder().Matches(value).Select(m => m.Value).Distinct().Order().ToList();
                var b = Placeholder().Matches(other).Select(m => m.Value).Distinct().Order().ToList();
                Assert.True(a.SequenceEqual(b), Path.GetFileName(neutral) + " / " + key + ": placeholders [" + string.Join(" ", a) + "] vs [" + string.Join(" ", b) + "]");
            }
        }
    }

    [Fact]
    public void NoValueIsEmpty()
    {
        foreach (string file in Directory.GetFiles(ResourcesDir, "*.resx"))
        {
            foreach ((string key, string value) in Read(file))
            {
                Assert.False(string.IsNullOrWhiteSpace(value), Path.GetFileName(file) + " / " + key + " is empty");
            }
        }
    }

    [Fact]
    public void KeysFollowTheNamingGuide()
    {
        foreach (string file in Directory.GetFiles(ResourcesDir, "*.resx"))
        {
            foreach (string key in Read(file).Keys)
            {
                Assert.Matches("^[A-Z][A-Za-z0-9]*(_One|_Other)?$", key);
            }
        }
    }

    [Fact]
    public void PluralKeysComeInPairs()
    {
        foreach (string neutral in NeutralFiles())
        {
            HashSet<string> keys = [.. Read(neutral).Keys];
            foreach (string key in keys.Where(k => k.EndsWith("_One", StringComparison.Ordinal)))
            {
                Assert.Contains(key[..^"_One".Length] + "_Other", keys);
            }
            foreach (string key in keys.Where(k => k.EndsWith("_Other", StringComparison.Ordinal)))
            {
                Assert.Contains(key[..^"_Other".Length] + "_One", keys);
            }
        }
    }
}
