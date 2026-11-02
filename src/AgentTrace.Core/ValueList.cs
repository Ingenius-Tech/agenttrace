using System.Collections;

namespace AgentTrace.Core;

/// <summary>
/// Immutable list with sequence equality. A C# record compares its lists by reference; Java compares
/// <c>List</c> by content. The core records that contain lists use this type to keep that equality.
/// </summary>
[System.Runtime.CompilerServices.CollectionBuilder(typeof(ValueList), nameof(ValueList.Create))]
public sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    public static readonly ValueList<T> Empty = new([]);

    private readonly T[] _items;

    public ValueList(IEnumerable<T> items) => _items = items.ToArray();

    public int Count => _items.Length;

    public T this[int index] => _items[index];

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    public bool Equals(ValueList<T>? other) =>
        other is not null && (ReferenceEquals(this, other) || _items.AsSpan().SequenceEqual(other._items));

    public override bool Equals(object? obj) => obj is ValueList<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (T item in _items) hash.Add(item);
        return hash.ToHashCode();
    }

    public override string ToString() => "[" + string.Join(", ", _items) + "]";
}

public static class ValueList
{
    public static ValueList<T> Of<T>(params T[] items) => new(items);

    /// <summary>Support for collection expressions: <c>ValueList&lt;string&gt; l = ["a", "b"];</c></summary>
    public static ValueList<T> Create<T>(ReadOnlySpan<T> items) => new(items.ToArray());

    public static ValueList<T> ToValueList<T>(this IEnumerable<T> items) => new(items);
}
