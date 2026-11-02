using System.Text;

namespace AgentTrace.Git;

/// <summary>Reads git output with NUL-separated fields (<c>-z</c> option), with no escaping in file names.</summary>
internal static class PorcelainParser
{
    /// <summary>A file with its status: A added, M modified, D deleted, T type change, U unmerged, ? untracked.</summary>
    internal sealed record Entry(char Status, string Path);

    /// <summary>Output of <c>git diff --name-status -z --no-renames</c>: status and path, alternating.</summary>
    internal static List<Entry> NameStatus(byte[] output)
    {
        List<string> tokens = Tokens(output);
        List<Entry> entries = [];
        for (int i = 0; i + 1 < tokens.Count; i += 2)
        {
            entries.Add(new Entry(tokens[i][0], tokens[i + 1]));
        }
        return entries;
    }

    /// <summary>Output of <c>git ls-files -z</c>: paths.</summary>
    internal static List<string> Paths(byte[] output) => Tokens(output);

    // Split on NUL over the bytes and only then decode each piece as UTF-8.
    private static List<string> Tokens(byte[] output)
    {
        List<string> tokens = [];
        int start = 0;
        for (int i = 0; i < output.Length; i++)
        {
            if (output[i] != 0) continue;
            if (i > start) tokens.Add(Encoding.UTF8.GetString(output, start, i - start));
            start = i + 1;
        }
        if (start < output.Length) tokens.Add(Encoding.UTF8.GetString(output, start, output.Length - start));
        return tokens;
    }
}
