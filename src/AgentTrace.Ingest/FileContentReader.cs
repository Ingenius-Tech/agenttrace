using System.Text;
using AgentTrace.Core;
using AgentTrace.Diff;

namespace AgentTrace.Ingest;

public enum LineEnding { None, Lf, CrLf, Cr, Mixed }

/// <summary>A reason or warning about a file's content. Presentation turns it into user text.</summary>
public enum ContentNote
{
    /// <summary>The file no longer exists.</summary>
    Missing,

    /// <summary>The path is a folder.</summary>
    IsFolder,

    /// <summary>The file is locked or being written; <c>ProblemDetail</c> carries the system message.</summary>
    CannotReadNow,

    /// <summary>Unexpected failure; <c>ProblemDetail</c> carries the exception text.</summary>
    CannotRead,

    /// <summary>The bytes are not valid UTF-8, so the file is shown as ISO-8859-1.</summary>
    NotUtf8,
}

/// <param name="Path">Absolute path of the file.</param>
/// <param name="Lines">Text lines (empty if it cannot be shown).</param>
/// <param name="Encoding">Encoding the file was read with.</param>
/// <param name="EncodingNote">Warning about the encoding (for example, that it was not UTF-8), or null.</param>
/// <param name="Truncated">The file is larger than <see cref="FileContentReader.MaxBytes"/> and only the beginning is shown.</param>
/// <param name="Binary">The content is binary: it is not shown.</param>
/// <param name="Size">Size on disk, in bytes.</param>
/// <param name="LinkTarget">Target if the file is a symbolic link, or null.</param>
/// <param name="Problem">Why there is no content to show (missing, locked...), or null. Presentation turns it into text.</param>
/// <param name="ProblemDetail">Raw exception text for the problems that carry one (<see cref="ContentNote.CannotReadNow"/>, <see cref="ContentNote.CannotRead"/>), or null.</param>
public sealed record FileContent(
    FilePath Path,
    ValueList<string> Lines,
    string Encoding,
    ContentNote? EncodingNote,
    LineEnding LineEnding,
    bool Truncated,
    bool Binary,
    long Size,
    string? LinkTarget,
    ContentNote? Problem,
    string? ProblemDetail)
{
    /// <summary>There is text to show.</summary>
    public bool Readable => Problem is null && !Binary;

    internal static FileContent Unavailable(FilePath path, ContentNote problem, string? detail = null) =>
        new(path, [], "", null, LineEnding.None, false, false, 0, null, problem, detail);
}

/// <summary>
/// Reads a text file for display, with limits and never throwing: whatever cannot be shown
/// (missing, binary, locked) comes back as a result with its reason. Read-only.
/// </summary>
public static class FileContentReader
{
    /// <summary>Nothing beyond this is loaded: the beginning is shown and the file is flagged as truncated.</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    private const int RetryDelayMillis = 200;

    /// <summary>Encoding label of a UTF-8 file that starts with a byte order mark (Presentation localizes it).</summary>
    public const string Utf8BomLabel = "UTF-8 with BOM";

    public static FileContent Read(FilePath file)
    {
        try
        {
            return DoRead(file);
        }
        catch (Exception e) when (IsMissing(e))
        {
            return FileContent.Unavailable(file, ContentNote.Missing);
        }
        catch (Exception first) when (first is IOException or UnauthorizedAccessException)
        {
            // A file that is being written (or locked by another process) is usually available a moment later
            Thread.Sleep(RetryDelayMillis);
            try
            {
                return DoRead(file);
            }
            catch (Exception e) when (IsMissing(e))
            {
                return FileContent.Unavailable(file, ContentNote.Missing);
            }
            catch (Exception second) when (second is IOException or UnauthorizedAccessException)
            {
                return FileContent.Unavailable(file, ContentNote.CannotReadNow, second.Message);
            }
            catch (Exception unexpected)
            {
                return FileContent.Unavailable(file, ContentNote.CannotRead, unexpected.ToString());
            }
        }
        catch (Exception unexpected)
        {
            return FileContent.Unavailable(file, ContentNote.CannotRead, unexpected.ToString());
        }
    }

    private static bool IsMissing(Exception e) => e is FileNotFoundException or DirectoryNotFoundException;

    private static FileContent DoRead(FilePath file)
    {
        string path = file.Value;
        if (Directory.Exists(path)) return FileContent.Unavailable(file, ContentNote.IsFolder);
        var info = new FileInfo(path);
        long size = info.Length;
        string? linkTarget = info.LinkTarget;

        byte[] bytes = ReadUpTo(path, MaxBytes + 1);
        bool truncated = bytes.Length > MaxBytes;
        int length = Math.Min(bytes.Length, MaxBytes);
        if (truncated) length = CutAtLastLineBreak(bytes, length);

        Bom? bom = Bom.Detect(bytes, length);
        if (bom is null && UnifiedDiffEngine.LooksBinary(bytes.AsSpan(0, Math.Min(length, 8_000))))
        {
            return new FileContent(file, [], "", null, LineEnding.None, false, true, size, linkTarget, null, null);
        }

        Encoding charset;
        string encodingName;
        ContentNote? note = null;
        int offset = 0;
        if (bom is not null)
        {
            charset = bom.Charset;
            encodingName = bom.Label;
            offset = bom.Length;
        }
        else if (IsStrictUtf8(bytes, length))
        {
            charset = new UTF8Encoding(false);
            encodingName = "UTF-8";
        }
        else
        {
            charset = System.Text.Encoding.Latin1;
            encodingName = "ISO-8859-1";
            note = ContentNote.NotUtf8;
        }
        string text = charset.GetString(bytes, offset, length - offset);
        return new FileContent(file, [.. SplitLines(text)], encodingName, note, DetectLineEnding(text), truncated, false,
            size, linkTarget, null, null);
    }

    private static byte[] ReadUpTo(string path, int max)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[(int)Math.Min(stream.Length, max)];
        int read = 0;
        while (read < buffer.Length)
        {
            int n = stream.Read(buffer, read, buffer.Length - read);
            if (n <= 0) break;
            read += n;
        }
        return read == buffer.Length ? buffer : buffer[..read];
    }

    /// <summary>When truncating, cut at the last line break so no line or character is left half-read.</summary>
    private static int CutAtLastLineBreak(byte[] bytes, int length)
    {
        for (int i = length - 1; i > 0; i--)
        {
            if (bytes[i] == (byte)'\n') return i + 1;
        }
        return length;
    }

    private static bool IsStrictUtf8(byte[] bytes, int length)
    {
        try
        {
            new UTF8Encoding(false, true).GetString(bytes, 0, length);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    internal static List<string> SplitLines(string text)
    {
        if (text.Length == 0) return [];
        List<string> lines = [];
        int start = 0;
        int n = text.Length;
        for (int i = 0; i < n; i++)
        {
            char c = text[i];
            if (c != '\n' && c != '\r') continue;
            lines.Add(text[start..i]);
            if (c == '\r' && i + 1 < n && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        // A trailing line break does not open a new empty line
        if (start < n) lines.Add(text[start..]);
        return lines;
    }

    internal static LineEnding DetectLineEnding(string text)
    {
        bool lf = false;
        bool crlf = false;
        bool cr = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                lf = true;
            }
            else if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf = true;
                    i++;
                }
                else
                {
                    cr = true;
                }
            }
        }
        int kinds = (lf ? 1 : 0) + (crlf ? 1 : 0) + (cr ? 1 : 0);
        if (kinds == 0) return LineEnding.None;
        if (kinds > 1) return LineEnding.Mixed;
        return lf ? LineEnding.Lf : crlf ? LineEnding.CrLf : LineEnding.Cr;
    }

    private sealed record Bom(Encoding Charset, string Label, int Length)
    {
        public static Bom? Detect(byte[] b, int length)
        {
            if (length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            {
                return new Bom(new UTF8Encoding(false), Utf8BomLabel, 3);
            }
            if (length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return new Bom(System.Text.Encoding.BigEndianUnicode, "UTF-16 BE", 2);
            if (length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return new Bom(System.Text.Encoding.Unicode, "UTF-16 LE", 2);
            return null;
        }
    }
}
