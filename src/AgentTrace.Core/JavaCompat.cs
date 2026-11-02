namespace AgentTrace.Core;

/// <summary>
/// Java behaviours that the application's output needs to be identical in C# (for example, a deterministic
/// layout that does not change between runs or between runtime versions).
/// </summary>
public static class JavaCompat
{
    /// <summary>
    /// Java's <c>String.hashCode()</c>: <c>s[0]*31^(n-1) + … + s[n-1]</c> with overflowing <c>int</c> arithmetic,
    /// over the UTF-16 units. <c>string.GetHashCode()</c> is never used, as it varies between runs.
    /// </summary>
    public static int StringHash(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        int hash = 0;
        foreach (char c in s) hash = unchecked(31 * hash + c);
        return hash;
    }

    /// <summary>Unsigned shift (Java's <c>&gt;&gt;&gt;</c>) of an <c>int</c>.</summary>
    public static int UnsignedShiftRight(int value, int count) => (int)((uint)value >> count);
}
