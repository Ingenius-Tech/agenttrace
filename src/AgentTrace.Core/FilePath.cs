namespace AgentTrace.Core;

/// <summary>
/// Absolute, normalized path of a file or folder. Plays the role of <c>java.nio.file.Path</c> in the core's maps and
/// sets: equality, hash and ordering are case-insensitive on Windows (case-sensitive on Linux and macOS).
/// Every path is absolute: Java's <c>toAbsolutePath().normalize()</c> is simply the construction here.
/// </summary>
public readonly struct FilePath : IEquatable<FilePath>, IComparable<FilePath>
{
    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string? _value;

    /// <summary>Normalizes with <see cref="Path.GetFullPath(string)"/> and removes the trailing separator (except at the root).</summary>
    public FilePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _value = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Length == 0 ? "." : path));
    }

    /// <summary>Text of the path, with the system separators.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>Last component (empty at the root).</summary>
    public string FileName => Path.GetFileName(Value);

    /// <summary>Folder that contains it, or null at the root.</summary>
    public FilePath? Parent => Path.GetDirectoryName(Value) is { Length: > 0 } dir ? new FilePath(dir) : null;

    /// <summary>
    /// True if the path is <paramref name="root"/> or is below it, comparing by components (like Java's
    /// <c>Path.startsWith</c>): <c>/a/bc</c> is not under <c>/a/b</c>.
    /// </summary>
    public bool IsUnder(FilePath root)
    {
        string v = Value;
        string r = root.Value;
        if (v.Length < r.Length || !v.StartsWith(r, Comparison)) return false;
        if (v.Length == r.Length) return true;
        return IsSeparator(r[^1]) || IsSeparator(v[r.Length]);
    }

    /// <summary>Path relative to <paramref name="root"/>, with the system separators (<c>Path.relativize</c>).</summary>
    public string RelativeTo(FilePath root) => Path.GetRelativePath(root.Value, Value);

    /// <summary>Joins a relative path (<c>Path.resolve</c>); if <paramref name="part"/> is absolute, it replaces it.</summary>
    public FilePath Combine(string part) => new(Path.Combine(Value, part));

    public FilePath Combine(params string[] parts) => new(Path.Combine([Value, .. parts]));

    public bool Equals(FilePath other) => string.Equals(Value, other.Value, Comparison);

    public override bool Equals(object? obj) => obj is FilePath other && Equals(other);

    public override int GetHashCode() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase.GetHashCode(Value) : StringComparer.Ordinal.GetHashCode(Value);

    public int CompareTo(FilePath other) => string.Compare(Value, other.Value, Comparison);

    public override string ToString() => Value;

    public static bool operator ==(FilePath left, FilePath right) => left.Equals(right);

    public static bool operator !=(FilePath left, FilePath right) => !left.Equals(right);

    private static bool IsSeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
}
