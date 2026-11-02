using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace AgentTrace.Core;

/// <summary>
/// Set that iterates in insertion order (Java's <c>LinkedHashSet</c>). <c>HashSet&lt;T&gt;</c> does not guarantee
/// that order; here it is contractual. No individual removal: nobody uses it.
/// </summary>
internal sealed class OrderedSet<T> : IEnumerable<T> where T : notnull
{
    private readonly HashSet<T> _set = [];
    private readonly List<T> _list = [];

    public int Count => _list.Count;

    public bool Add(T item)
    {
        if (!_set.Add(item)) return false;
        _list.Add(item);
        return true;
    }

    public bool Contains(T item) => _set.Contains(item);

    public void Clear()
    {
        _set.Clear();
        _list.Clear();
    }

    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Dictionary that iterates in insertion order (Java's <c>LinkedHashMap</c>): reassigning a key keeps its
/// position. No individual removal: nobody uses it.
/// </summary>
internal sealed class OrderedMap<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _map = [];
    private readonly List<TKey> _keys = [];

    public int Count => _keys.Count;

    public TValue this[TKey key]
    {
        get => _map[key];
        set
        {
            if (!_map.ContainsKey(key)) _keys.Add(key);
            _map[key] = value;
        }
    }

    public void Clear()
    {
        _map.Clear();
        _keys.Clear();
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => _map.TryGetValue(key, out value);

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> create)
    {
        if (_map.TryGetValue(key, out TValue? existing)) return existing;
        TValue created = create(key);
        this[key] = created;
        return created;
    }

    public IEnumerable<KeyValuePair<TKey, TValue>> Entries => _keys.Select(k => new KeyValuePair<TKey, TValue>(k, _map[k]));

    public IEnumerable<TValue> Values => _keys.Select(k => _map[k]);
}
