using System.Text;
using AgentTrace.Core;

namespace AgentTrace.Ingest;

/// <summary>A complete line of a trace and its number (from 1).</summary>
public sealed record RawLine(long Number, string Text);

/// <summary>
/// Reads the new lines of a file that grows by appending. Read-only: it never creates or modifies the file.
/// The first call to <see cref="Poll"/> returns the existing content; later ones, only what was added.
/// A line without a trailing line break is kept pending until it is completed.
/// Not thread-safe.
/// </summary>
public sealed class TraceFileTailer(FilePath file)
{
    private const int Chunk = 1 << 20;

    private long _offset;
    private long _lineNumber;
    private byte[] _pending = [];

    /// <summary>Whether the last call to <see cref="Poll"/> detected that the file had been truncated.</summary>
    public bool Truncated { get; private set; }

    /// <summary>Complete lines added since the last call. Empty if the file does not exist yet.</summary>
    public IReadOnlyList<RawLine> Poll()
    {
        Truncated = false;
        List<RawLine> lines = [];
        try
        {
            using var stream = new FileStream(file.Value, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            long size = stream.Length;
            if (size < _offset)
            {
                _offset = 0;
                _lineNumber = 0;
                _pending = [];
                Truncated = true;
            }
            var buffer = new byte[Chunk];
            while (_offset < size)
            {
                stream.Seek(_offset, SeekOrigin.Begin);
                int n = stream.Read(buffer, 0, buffer.Length);
                if (n <= 0) break;
                _offset += n;
                Split(buffer, n, lines);
            }
        }
        catch (FileNotFoundException)
        {
            // The trace does not exist yet: it is not created
        }
        catch (DirectoryNotFoundException)
        {
        }
        return lines;
    }

    /// <summary>Returns and discards the final line without a line break, if any (for one-off reads).</summary>
    public RawLine? FlushPending()
    {
        if (_pending.Length == 0) return null;
        var line = new RawLine(++_lineNumber, Decode(_pending));
        _pending = [];
        return line;
    }

    private void Split(byte[] data, int length, List<RawLine> output)
    {
        int start = 0;
        for (int i = 0; i < length; i++)
        {
            if (data[i] != (byte)'\n') continue;
            byte[] line = Join(_pending, data, start, i - start);
            _pending = [];
            output.Add(new RawLine(++_lineNumber, Decode(line)));
            start = i + 1;
        }
        if (start < length) _pending = Join(_pending, data, start, length - start);
    }

    private static byte[] Join(byte[] head, byte[] data, int from, int count)
    {
        var result = new byte[head.Length + count];
        Buffer.BlockCopy(head, 0, result, 0, head.Length);
        Buffer.BlockCopy(data, from, result, head.Length, count);
        return result;
    }

    private static string Decode(byte[] bytes)
    {
        int end = bytes.Length > 0 && bytes[^1] == (byte)'\r' ? bytes.Length - 1 : bytes.Length;
        return Encoding.UTF8.GetString(bytes, 0, end);
    }
}
