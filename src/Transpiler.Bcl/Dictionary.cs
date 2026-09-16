using System;
using System.Collections;
using System.Collections.Generic;

namespace Transpiler.Bcl;

public readonly struct KeyValuePair<TKey, TValue>
{
    public KeyValuePair(TKey key, TValue value) { Key = key; Value = value; }
    public TKey Key { get; }
    public TValue Value { get; }
    public void Deconstruct(out TKey key, out TValue value) { key = Key; value = Value; }
    public override string ToString() => string.Concat(string.Concat("[", Key is null ? "" : Key.ToString(), ", "), Value is null ? "" : Value.ToString(), "]");
}

/// <summary>Managed bucket/entry dictionary. Hashes are cached; rehash never invokes user code.</summary>
public class Dictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
{
    private struct Entry { public int Hash; public int Next; public bool Active; public TKey Key; public TValue Value; }
    private Entry[] _entries;
    private int[] _buckets;
    private int _used;
    private int _free = -1;
    private int _freeCount;
    private int _version;
    private readonly IEqualityComparer<TKey> _comparer;
    private readonly bool _allowNull;
    private KeyCollection? _keys;
    private ValueCollection? _values;
    public Dictionary() : this(0, null) { }
    public Dictionary(int capacity) : this(capacity, null) { }
    public Dictionary(IEqualityComparer<TKey>? comparer) : this(0, comparer) { }
    public Dictionary(int capacity, IEqualityComparer<TKey>? comparer) : this(capacity, comparer, false) { }
    internal Dictionary(int capacity, IEqualityComparer<TKey>? comparer, bool allowNull)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _comparer = comparer ?? EqualityComparer<TKey>.Default;
        _allowNull = allowNull;
        _entries = new Entry[capacity]; _buckets = new int[capacity];
    }
    public Dictionary(IEnumerable<global::System.Collections.Generic.KeyValuePair<TKey, TValue>> values) : this(values, null) { }
    public Dictionary(IEnumerable<global::System.Collections.Generic.KeyValuePair<TKey, TValue>> values, IEqualityComparer<TKey>? comparer) : this(0, comparer)
    { if (values == null) throw new ArgumentNullException(nameof(values)); foreach (var item in values) Add(item.Key, item.Value); }
    public Dictionary(IDictionary<TKey, TValue> values) : this((IEnumerable<global::System.Collections.Generic.KeyValuePair<TKey, TValue>>)values, null) { }
    public Dictionary(IDictionary<TKey, TValue> values, IEqualityComparer<TKey>? comparer) : this((IEnumerable<global::System.Collections.Generic.KeyValuePair<TKey, TValue>>)values, comparer) { }
    public int Count => _used - _freeCount;
    public int Capacity => _entries.Length;
    public IEqualityComparer<TKey> Comparer => _comparer;
    public KeyCollection Keys => _keys ?? (_keys = new KeyCollection(this));
    public ValueCollection Values => _values ?? (_values = new ValueCollection(this));
    ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;
    ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;
    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;
    bool ICollection<global::System.Collections.Generic.KeyValuePair<TKey, TValue>>.IsReadOnly => false;
    public TValue this[TKey key]
    {
        get { int index = Find(key, Hash(key)); if (index < 0) throw new KeyNotFoundException(); return _entries[index].Value; }
        set { Insert(key, value, 1); }
    }
    private int Hash(TKey key)
    {
        if (key is null)
        { if (!_allowNull) throw new ArgumentNullException(nameof(key)); return 0; }
        return _comparer.GetHashCode(key) & int.MaxValue;
    }
    private int Find(TKey key, int hash)
    {
        if (_buckets.Length == 0) return -1;
        int index = _buckets[hash % _buckets.Length] - 1;
        int traversed = 0;
        while (index >= 0)
        {
            if ((uint)index >= (uint)_used || traversed++ >= _entries.Length) throw new InvalidOperationException("Invalid or concurrently modified collision chain.");
            if (_entries[index].Hash == hash && _comparer.Equals(_entries[index].Key, key)) return index;
            index = _entries[index].Next;
        }
        return -1;
    }
    private bool Insert(TKey key, TValue value, int mode)
    {
        int hash = Hash(key);
        int existing = Find(key, hash);
        if (existing >= 0)
        {
            if (mode == 1) { _entries[existing].Value = value; return true; }
            if (mode == 2) throw new ArgumentException("An item with the same key has already been added.");
            return false;
        }
        if (_freeCount == 0 && _used == _entries.Length) Resize(_entries.Length == 0 ? 4 : checked(_entries.Length * 2), false);
        int index;
        if (_freeCount != 0) { index = _free; _free = _entries[index].Next; _freeCount--; }
        else index = _used++;
        int bucket = hash % _buckets.Length;
        _entries[index] = new Entry { Hash = hash, Next = _buckets[bucket] - 1, Active = true, Key = key, Value = value };
        _buckets[bucket] = index + 1; _version++;
        return true;
    }
    private void Resize(int capacity, bool compact)
    {
        var entries = new Entry[capacity]; var buckets = new int[capacity];
        int used = 0;
        for (int i = 0; i < _used; i++)
        {
            if (compact && !_entries[i].Active) continue;
            int slot = compact ? used++ : i;
            entries[slot] = _entries[i];
            if (entries[slot].Active)
            { int bucket = entries[slot].Hash % capacity; entries[slot].Next = buckets[bucket] - 1; buckets[bucket] = slot + 1; }
        }
        _entries = entries; _buckets = buckets;
        if (compact) { _used = used; _free = -1; _freeCount = 0; }
    }
    public void Add(TKey key, TValue value) => Insert(key, value, 2);
    public bool TryAdd(TKey key, TValue value) => Insert(key, value, 0);
    public bool ContainsKey(TKey key) => Find(key, Hash(key)) >= 0;
    public bool TryGetValue(TKey key, out TValue value)
    { int index = Find(key, Hash(key)); value = index < 0 ? default! : _entries[index].Value; return index >= 0; }
    public bool ContainsValue(TValue value)
    { for (int i = 0; i < _used; i++) if (_entries[i].Active && EqualityComparer<TValue>.Default.Equals(_entries[i].Value, value)) return true; return false; }
    public bool Remove(TKey key) => Remove(key, out _);
    public bool Remove(TKey key, out TValue value)
    {
        int hash = Hash(key); value = default!;
        if (_buckets.Length == 0) return false;
        int bucket = hash % _buckets.Length, previous = -1, index = _buckets[bucket] - 1, traversed = 0;
        while (index >= 0)
        {
            if ((uint)index >= (uint)_used || traversed++ >= _entries.Length) throw new InvalidOperationException("Invalid or concurrently modified collision chain.");
            if (_entries[index].Hash == hash && _comparer.Equals(_entries[index].Key, key))
            {
                if (previous < 0) _buckets[bucket] = _entries[index].Next + 1;
                else _entries[previous].Next = _entries[index].Next;
                value = _entries[index].Value;
                _entries[index] = default; _entries[index].Next = _free;
                _free = index; _freeCount++;
                return true; // Modern .NET removal does not invalidate enumerators.
            }
            previous = index; index = _entries[index].Next;
        }
        return false;
    }
    internal bool TryGetStoredKey(TKey key, out TKey stored)
    { int index = Find(key, Hash(key)); stored = index < 0 ? default! : _entries[index].Key; return index >= 0; }
    public void Clear()
    {
        for (int i = 0; i < _used; i++) _entries[i] = default;
        for (int i = 0; i < _buckets.Length; i++) _buckets[i] = 0;
        _used = 0; _freeCount = 0; _free = -1;
    }
    public int EnsureCapacity(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (capacity > _entries.Length) { Resize(capacity, false); _version++; }
        return _entries.Length;
    }
    public void TrimExcess() => TrimExcess(Count);
    public void TrimExcess(int capacity)
    {
        if (capacity < Count) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (capacity < _entries.Length) { Resize(capacity, true); _version++; }
    }
    void ICollection<global::System.Collections.Generic.KeyValuePair<TKey, TValue>>.Add(global::System.Collections.Generic.KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
    bool ICollection<global::System.Collections.Generic.KeyValuePair<TKey, TValue>>.Contains(global::System.Collections.Generic.KeyValuePair<TKey, TValue> item) => TryGetValue(item.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, item.Value);
    bool ICollection<global::System.Collections.Generic.KeyValuePair<TKey, TValue>>.Remove(global::System.Collections.Generic.KeyValuePair<TKey, TValue> item) => TryGetValue(item.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, item.Value) && Remove(item.Key);
    public void CopyTo(global::System.Collections.Generic.KeyValuePair<TKey, TValue>[] array, int index)
    {
        CheckCopy(array, index, Count);
        foreach (var item in this) array[index++] = item;
    }
    private static void CheckCopy<T>(T[] array, int index, int count)
    {
        if (array == null) throw new ArgumentNullException(nameof(array));
        if (index < 0 || index > array.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (count > array.Length - index) throw new ArgumentException("Destination array is too small.");
    }
    public Enumerator GetEnumerator() => new Enumerator(this);
    IEnumerator<global::System.Collections.Generic.KeyValuePair<TKey, TValue>> IEnumerable<global::System.Collections.Generic.KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public struct Enumerator : IEnumerator<global::System.Collections.Generic.KeyValuePair<TKey, TValue>>
    {
        private readonly Dictionary<TKey, TValue> _owner;
        private readonly int _version;
        private int _index;
        private global::System.Collections.Generic.KeyValuePair<TKey, TValue> _current;
        internal Enumerator(Dictionary<TKey, TValue> owner) { _owner = owner; _version = owner._version; _index = 0; _current = default; }
        public global::System.Collections.Generic.KeyValuePair<TKey, TValue> Current => _current;
        object IEnumerator.Current => _index == 0 || _index == _owner._used + 1 ? throw new InvalidOperationException() : _current;
        public bool MoveNext()
        {
            if (_version != _owner._version) throw new InvalidOperationException("Collection was modified.");
            while (_index < _owner._used)
            {
                int index = _index++;
                if (_owner._entries[index].Active)
                { _current = new global::System.Collections.Generic.KeyValuePair<TKey, TValue>(_owner._entries[index].Key, _owner._entries[index].Value); return true; }
            }
            _index = _owner._used + 1; _current = default; return false;
        }
        void IEnumerator.Reset()
        { if (_version != _owner._version) throw new InvalidOperationException("Collection was modified."); _index = 0; _current = default; }
        public void Dispose() { }
    }
    public sealed class KeyCollection : ICollection<TKey>, IReadOnlyCollection<TKey>
    {
        private readonly Dictionary<TKey, TValue> _owner;
        public KeyCollection(Dictionary<TKey, TValue> dictionary) { _owner = dictionary ?? throw new ArgumentNullException(nameof(dictionary)); }
        public int Count => _owner.Count;
        bool ICollection<TKey>.IsReadOnly => true;
        public bool Contains(TKey item) => _owner.ContainsKey(item);
        public void CopyTo(TKey[] array, int index) { CheckCopy(array, index, Count); foreach (var item in _owner) array[index++] = item.Key; }
        void ICollection<TKey>.Add(TKey item) => throw new NotSupportedException();
        bool ICollection<TKey>.Remove(TKey item) => throw new NotSupportedException();
        void ICollection<TKey>.Clear() => throw new NotSupportedException();
        public Enumerator GetEnumerator() => new Enumerator(_owner);
        IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public struct Enumerator : IEnumerator<TKey>
        {
            private Dictionary<TKey, TValue>.Enumerator _inner;
            internal Enumerator(Dictionary<TKey, TValue> owner) { _inner = owner.GetEnumerator(); }
            public TKey Current => _inner.Current.Key;
            object? IEnumerator.Current { get { var ignored = ((IEnumerator)_inner).Current; return Current; } }
            public bool MoveNext() => _inner.MoveNext();
            public void Dispose() { }
            void IEnumerator.Reset() { var boxed = (IEnumerator)_inner; boxed.Reset(); _inner = (Dictionary<TKey, TValue>.Enumerator)boxed; }
        }
    }
    public sealed class ValueCollection : ICollection<TValue>, IReadOnlyCollection<TValue>
    {
        private readonly Dictionary<TKey, TValue> _owner;
        public ValueCollection(Dictionary<TKey, TValue> dictionary) { _owner = dictionary ?? throw new ArgumentNullException(nameof(dictionary)); }
        public int Count => _owner.Count;
        bool ICollection<TValue>.IsReadOnly => true;
        public bool Contains(TValue item) => _owner.ContainsValue(item);
        public void CopyTo(TValue[] array, int index) { CheckCopy(array, index, Count); foreach (var item in _owner) array[index++] = item.Value; }
        void ICollection<TValue>.Add(TValue item) => throw new NotSupportedException();
        bool ICollection<TValue>.Remove(TValue item) => throw new NotSupportedException();
        void ICollection<TValue>.Clear() => throw new NotSupportedException();
        public Enumerator GetEnumerator() => new Enumerator(_owner);
        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public struct Enumerator : IEnumerator<TValue>
        {
            private Dictionary<TKey, TValue>.Enumerator _inner;
            internal Enumerator(Dictionary<TKey, TValue> owner) { _inner = owner.GetEnumerator(); }
            public TValue Current => _inner.Current.Value;
            object? IEnumerator.Current { get { var ignored = ((IEnumerator)_inner).Current; return Current; } }
            public bool MoveNext() => _inner.MoveNext();
            public void Dispose() { }
            void IEnumerator.Reset() { var boxed = (IEnumerator)_inner; boxed.Reset(); _inner = (Dictionary<TKey, TValue>.Enumerator)boxed; }
        }
    }
}
