namespace StorageChecker.Core.Usn;

/// <summary>
/// Cache LRU sederhana & thread-safe untuk memetakan FRN → path.
/// Kapasitas dibatasi agar memori stabil meski jutaan file ter-resolve.
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();
    private readonly object _lock = new();

    public LruCache(int capacity)
    {
        _capacity = Math.Max(16, capacity);
        _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(_capacity);
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    public void Add(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                existing.Value = (key, value);
                _order.AddFirst(existing);
                return;
            }

            if (_map.Count >= _capacity)
            {
                var last = _order.Last;
                if (last is not null)
                {
                    _order.RemoveLast();
                    _map.Remove(last.Value.Key);
                }
            }

            var node = new LinkedListNode<(TKey, TValue)>((key, value));
            _order.AddFirst(node);
            _map[key] = node;
        }
    }

    public int Count
    {
        get { lock (_lock) { return _map.Count; } }
    }
}
