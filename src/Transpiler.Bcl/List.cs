using System;
using System.Collections;
using System.Collections.Generic;

namespace Transpiler.Bcl;

/// <summary>Portable managed implementation; every method is compiled from this IL for each backend.</summary>
public class List<T> : IEnumerable<T>
{
    private T[] _items;
    private int _count;
    private int _version;
    public List() : this(0) { }
    public List(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new T[capacity];
    }
    public List(IEnumerable<T> values) : this()
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        foreach (T value in values) Add(value);
    }
    public int Count => _count;
    public int Capacity
    {
        get => _items.Length;
        set
        {
            if (value < _count) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _items.Length) return;
            var storage = new T[value];
            for (int i = 0; i < _count; i++) storage[i] = _items[i];
            _items = storage;
        }
    }
    public T this[int index]
    {
        get { Check(index); return _items[index]; }
        set { Check(index); _items[index] = value; _version++; }
    }
    private void Check(int index)
    { if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index)); }
    private void Grow(int size)
    {
        if (size <= _items.Length) return;
        int capacity = _items.Length == 0 ? 4 : checked(_items.Length * 2);
        if (capacity < size) capacity = size;
        Capacity = capacity;
    }
    public void Add(T value)
    { Grow(checked(_count + 1)); _items[_count++] = value; _version++; }
    public void AddRange(IEnumerable<T> values)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        // Snapshot self-addition; the normal enumerator intentionally invalidates on mutation.
        if (object.ReferenceEquals(values, this))
        { int count = _count; Grow(checked(count * 2)); for (int i = 0; i < count; i++) Add(_items[i]); return; }
        foreach (T value in values) Add(value);
    }
    public void Insert(int index, T value)
    {
        if ((uint)index > (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        Grow(checked(_count + 1));
        for (int i = _count; i > index; i--) _items[i] = _items[i - 1];
        _items[index] = value; _count++; _version++;
    }
    public void RemoveAt(int index)
    {
        Check(index); _count--;
        for (int i = index; i < _count; i++) _items[i] = _items[i + 1];
        _items[_count] = default!; _version++;
    }
    public int RemoveAll(Predicate<T> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        int write = 0;
        for (int read = 0; read < _count; read++) if (!predicate(_items[read])) _items[write++] = _items[read];
        int removed = _count - write;
        for (int i = write; i < _count; i++) _items[i] = default!;
        if (removed != 0) { _count = write; _version++; }
        return removed;
    }
    public void Clear()
    { for (int i = 0; i < _count; i++) _items[i] = default!; _count = 0; _version++; }
    public T[] ToArray()
    { var result = new T[_count]; for (int i = 0; i < _count; i++) result[i] = _items[i]; return result; }
    public void ForEach(Action<T> action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        int version = _version;
        for (int i = 0; i < _count && version == _version; i++) action(_items[i]);
        if (version != _version) throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
    }
    public Enumerator GetEnumerator() => new Enumerator(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public struct Enumerator : IEnumerator<T>
    {
        private readonly List<T> _owner;
        private readonly int _version;
        private int _index;
        private T _current;
        internal Enumerator(List<T> owner) { _owner = owner; _version = owner._version; _index = 0; _current = default!; }
        public T Current => _current;
        object? IEnumerator.Current
        {
            get
            {
                if (_index == 0 || _index == _owner._count + 1) throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                return _current;
            }
        }
        public bool MoveNext()
        {
            if (_version != _owner._version) throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
            if (_index < _owner._count) { _current = _owner._items[_index++]; return true; }
            _index = _owner._count + 1; _current = default!; return false;
        }
        void IEnumerator.Reset()
        {
            if (_version != _owner._version) throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
            _index = 0; _current = default!;
        }
        public void Dispose() { }
    }
}
