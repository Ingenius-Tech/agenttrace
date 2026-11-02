using System.Text.RegularExpressions;
using AgentTrace.Core;
using DiffPlex;
using DiffPlex.Chunkers;

namespace AgentTrace.Diff;

public enum LineType { Unchanged, Addition, Deletion }

public sealed record DiffLine(LineType Type, string Content, int? OldNumber, int? NewNumber);

public sealed record DiffHunk(int OldStart, int OldCount, int NewStart, int NewCount, IReadOnlyList<DiffLine> Lines);

/// <param name="Unavailable">Why the diff was not computed (for example, too large), or null.</param>
public sealed record DiffResult(
    string FilePath,
    int Additions,
    int Deletions,
    IReadOnlyList<DiffHunk> Hunks,
    IReadOnlyList<DiffLine> AllLines,
    bool IsNewFile,
    bool IsDeleted,
    UnavailableReason? Unavailable);

/// <summary>
/// Line-by-line differences between two texts (DiffPlex, Myers algorithm) shaped like a unified diff.
/// Text is split on any line break, so a change of line endings only produces no differences.
/// </summary>
public static partial class UnifiedDiffEngine
{
    /// <summary>Context lines around each change in a hunk.</summary>
    public const int Context = 3;

    /// <summary>Above this number of lines (in either text) the diff is not computed.</summary>
    public const int MaxLines = 20_000;

    private const int BinaryScanBytes = 8_000;

    // Equivalent to Java's \R: CRLF or any of LF, VT, FF, CR, NEL, LS, PS.
    [GeneratedRegex(@"\r\n|[\n\u000B\u000C\r\u0085\u20" + "28" + @"\u20" + "29]")]
    private static partial Regex LineBreak();

    /// <summary>Same rule as git: a NUL byte in the first 8,000 bytes means binary content.</summary>
    public static bool LooksBinary(ReadOnlySpan<byte> content)
    {
        var limit = Math.Min(content.Length, BinaryScanBytes);
        return content[..limit].Contains((byte)0);
    }

    /// <summary>A null text means the file did not exist on that side.</summary>
    public static DiffResult ComputeDiff(string? original, string? modified, string filePath)
    {
        var isNew = original is null && modified is not null;
        var isDeleted = original is not null && modified is null;
        var oldLines = Lines(original);
        var newLines = Lines(modified);

        if (oldLines.Length > MaxLines || newLines.Length > MaxLines)
            return new DiffResult(filePath, 0, 0, [], [], isNew, isDeleted, UnavailableReason.TooLarge);

        var all = new List<DiffLine>(Math.Max(oldLines.Length, newLines.Length) + 16);
        var oldPos = 0;
        var newPos = 0;
        var adds = 0;
        var dels = 0;
        foreach (var block in Blocks(oldLines, newLines))
        {
            while (oldPos < block.DeleteStartA)
            {
                all.Add(new DiffLine(LineType.Unchanged, oldLines[oldPos], oldPos + 1, newPos + 1));
                oldPos++;
                newPos++;
            }
            for (var i = 0; i < block.DeleteCountA; i++)
            {
                all.Add(new DiffLine(LineType.Deletion, oldLines[oldPos], ++oldPos, null));
                dels++;
            }
            for (var i = 0; i < block.InsertCountB; i++)
            {
                all.Add(new DiffLine(LineType.Addition, newLines[newPos], null, ++newPos));
                adds++;
            }
        }
        while (oldPos < oldLines.Length)
        {
            all.Add(new DiffLine(LineType.Unchanged, oldLines[oldPos], oldPos + 1, newPos + 1));
            oldPos++;
            newPos++;
        }
        return new DiffResult(filePath, adds, dels, Hunks(all), all, isNew, isDeleted, null);
    }

    private static string[] Lines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var parts = LineBreak().Split(text);
        // A trailing line break does not open a new empty line
        return parts[^1].Length == 0 ? parts[..^1] : parts;
    }

    private static IList<DiffPlex.Model.DiffBlock> Blocks(string[] oldLines, string[] newLines)
    {
        // DiffPlex splits the text with an IChunker: it is given two markers and returns the already
        // split lines, to avoid joining and splitting them again (an empty line would be ambiguous).
        var result = new Differ().CreateDiffs("old", "new", false, false, new PresplitChunker(oldLines, newLines));
        return result.DiffBlocks;
    }

    private sealed class PresplitChunker(string[] oldLines, string[] newLines) : IChunker
    {
        public IReadOnlyList<string> Chunk(string str) => str == "old" ? oldLines : newLines;
    }

    private static List<DiffHunk> Hunks(List<DiffLine> all)
    {
        var hunks = new List<DiffHunk>();
        var i = 0;
        var oldBefore = 0;
        var newBefore = 0;
        while (i < all.Count)
        {
            if (all[i].Type == LineType.Unchanged)
            {
                oldBefore++;
                newBefore++;
                i++;
                continue;
            }
            // Step back to include the preceding context
            var start = i;
            var back = 0;
            while (start > 0 && back < Context)
            {
                start--;
                back++;
                oldBefore--;
                newBefore--;
            }
            // Advance until there are more than 2*Context unchanged lines in a row (or the end)
            var end = i;
            var lastChange = i;
            while (end < all.Count)
            {
                if (all[end].Type != LineType.Unchanged) lastChange = end;
                else if (end - lastChange > 2 * Context) break;
                end++;
            }
            var hunkEnd = Math.Min(all.Count, lastChange + Context + 1);
            var slice = all.GetRange(start, hunkEnd - start);
            var oldCount = 0;
            var newCount = 0;
            foreach (var l in slice)
            {
                if (l.Type != LineType.Addition) oldCount++;
                if (l.Type != LineType.Deletion) newCount++;
            }
            hunks.Add(new DiffHunk(oldBefore + 1, oldCount, newBefore + 1, newCount, slice));
            oldBefore += oldCount;
            newBefore += newCount;
            i = hunkEnd;
        }
        return hunks;
    }
}
