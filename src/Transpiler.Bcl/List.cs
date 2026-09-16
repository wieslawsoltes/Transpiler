using System;
using System.Collections;
using System.Collections.Generic;

namespace Transpiler.Bcl;

/// <summary>Portable managed implementation; every method is compiled from this IL for each backend.</summary>
public class List<T> : IList<T>, IReadOnlyList<T>
{
    public ReadOnlyCollection<T> AsReadOnly() => new ReadOnlyCollection<T>(this);
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
    bool ICollection<T>.IsReadOnly => false;
    public bool Contains(T item) => IndexOf(item) >= 0;
    public int IndexOf(T item) => IndexOf(item, 0, _count);
    public int IndexOf(T item, int index) => IndexOf(item, index, _count - index);
    public int IndexOf(T item, int index, int count)
    {
        if (index < 0 || index > _count) throw new ArgumentOutOfRangeException(nameof(index));
        if (count < 0 || count > _count - index) throw new ArgumentOutOfRangeException(nameof(count));
        for (int i = index; i < index + count; i++) if (EqualityComparer<T>.Default.Equals(_items[i], item)) return i;
        return -1;
    }
    public int LastIndexOf(T item) => LastIndexOf(item, _count - 1, _count);
    public int LastIndexOf(T item, int index) => LastIndexOf(item, index, index + 1);
    public int LastIndexOf(T item, int index, int count)
    {
        if (_count == 0) return -1;
        if (index < 0 || index >= _count) throw new ArgumentOutOfRangeException(nameof(index));
        if (count < 0 || count > index + 1) throw new ArgumentOutOfRangeException(nameof(count));
        for (int i = index; i > index - count; i--) if (EqualityComparer<T>.Default.Equals(_items[i], item)) return i;
        return -1;
    }
    private void Range(int index, int count)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (index > _count - count) throw new ArgumentException("Range exceeds the list.");
    }
    public bool Remove(T item)
    { int index = IndexOf(item); if (index < 0) return false; RemoveAt(index); return true; }
    public void CopyTo(T[] array) => CopyTo(array, 0);
    public void CopyTo(T[] array, int arrayIndex) => CopyTo(0, array, arrayIndex, _count);
    public void CopyTo(int index, T[] array, int arrayIndex, int count)
    {
        Range(index, count);
        if (array == null) throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (arrayIndex > array.Length - count) throw new ArgumentException("Destination array is too small.");
        for (int i = 0; i < count; i++) array[arrayIndex + i] = _items[index + i];
    }
    public void InsertRange(int index, IEnumerable<T> collection)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));
        if ((uint)index > (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
        if (object.ReferenceEquals(collection, this))
        { var snapshot = ToArray(); foreach (T item in snapshot) Insert(index++, item); return; }
        foreach (T item in collection) Insert(index++, item);
    }
    public void RemoveRange(int index, int count)
    {
        Range(index, count); if (count == 0) return;
        for (int i = index; i < _count - count; i++) _items[i] = _items[i + count];
        for (int i = _count - count; i < _count; i++) _items[i] = default!;
        _count -= count; _version++;
    }
    public List<T> GetRange(int index, int count)
    { Range(index, count); var result = new List<T>(count); for (int i = index; i < index + count; i++) result.Add(_items[i]); return result; }
    public List<T> Slice(int start, int length) => GetRange(start, length);
    public int EnsureCapacity(int capacity)
    { if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity)); Grow(capacity); return Capacity; }
    public void TrimExcess() { if (_count < (long)_items.Length * 9 / 10) Capacity = _count; }
    public bool Exists(Predicate<T> match) => FindIndex(match) >= 0;
    public T? Find(Predicate<T> match) { int index = FindIndex(match); return index < 0 ? default : _items[index]; }
    public int FindIndex(Predicate<T> match) => FindIndex(0, _count, match);
    public int FindIndex(int startIndex, Predicate<T> match) => FindIndex(startIndex, _count - startIndex, match);
    public int FindIndex(int startIndex, int count, Predicate<T> match)
    {
        if (startIndex < 0 || startIndex > _count) throw new ArgumentOutOfRangeException(nameof(startIndex));
        if (count < 0 || startIndex > _count - count) throw new ArgumentOutOfRangeException(nameof(count));
        if (match == null) throw new ArgumentNullException(nameof(match));
        for (int i = startIndex; i < startIndex + count; i++) if (match(_items[i])) return i;
        return -1;
    }
    public T? FindLast(Predicate<T> match) { int index = FindLastIndex(match); return index < 0 ? default : _items[index]; }
    public int FindLastIndex(Predicate<T> match) => FindLastIndex(_count - 1, _count, match);
    public int FindLastIndex(int startIndex, Predicate<T> match) => FindLastIndex(startIndex, startIndex + 1, match);
    public int FindLastIndex(int startIndex, int count, Predicate<T> match)
    {
        if (match == null) throw new ArgumentNullException(nameof(match));
        if (_count == 0 ? startIndex != -1 : startIndex < 0 || startIndex >= _count) throw new ArgumentOutOfRangeException(nameof(startIndex));
        if (count < 0 || count > startIndex + 1) throw new ArgumentOutOfRangeException(nameof(count));
        for (int i = startIndex; i > startIndex - count; i--) if (match(_items[i])) return i;
        return -1;
    }
    public List<T> FindAll(Predicate<T> match)
    {
        if (match == null) throw new ArgumentNullException(nameof(match));
        var result = new List<T>(); for (int i = 0; i < _count; i++) if (match(_items[i])) result.Add(_items[i]); return result;
    }
    public bool TrueForAll(Predicate<T> match)
    { if (match == null) throw new ArgumentNullException(nameof(match)); for (int i = 0; i < _count; i++) if (!match(_items[i])) return false; return true; }
    public List<R> ConvertAll<R>(Converter<T, R> converter)
    { if (converter == null) throw new ArgumentNullException(nameof(converter)); var result = new List<R>(_count); for (int i = 0; i < _count; i++) result.Add(converter(_items[i])); return result; }
    public void Reverse() => Reverse(0, _count);
    public void Reverse(int index, int count)
    { Range(index, count); for (int i = 0; i < count / 2; i++) Swap(index + i, index + count - 1 - i); _version++; }
    private void Swap(int a, int b) { T value = _items[a]; _items[a] = _items[b]; _items[b] = value; }
    public void Sort() => Sort(0, _count, null);
    public void Sort(IComparer<T>? comparer) => Sort(0, _count, comparer);
    public void Sort(Comparison<T> comparison)
    { if (comparison == null) throw new ArgumentNullException(nameof(comparison)); Sort(Comparer<T>.Create(comparison)); }
    public void Sort(int index, int count, IComparer<T>? comparer)
    {
        Range(index, count); comparer ??= Comparer<T>.Default;
        // Heapsort: bounded O(n log n), no recursion and no temporary arrays. Stability is not promised.
        try
        {
            for (int parent = count / 2 - 1; parent >= 0; parent--) Sift(index, parent, count, comparer);
            for (int end = count - 1; end > 0; end--) { Swap(index, index + end); Sift(index, 0, end, comparer); }
        }
        catch (Exception) { throw new InvalidOperationException("Failed to compare two elements in the array."); }
        _version++;
    }
    private void Sift(int offset, int parent, int count, IComparer<T> comparer)
    {
        while (parent < count / 2)
        {
            int child = parent * 2 + 1;
            if (child + 1 < count && comparer.Compare(_items[offset + child], _items[offset + child + 1]) < 0) child++;
            if (comparer.Compare(_items[offset + parent], _items[offset + child]) >= 0) return;
            Swap(offset + parent, offset + child); parent = child;
        }
    }
    public int BinarySearch(T item) => BinarySearch(0, _count, item, null);
    public int BinarySearch(T item, IComparer<T>? comparer) => BinarySearch(0, _count, item, comparer);
    public int BinarySearch(int index, int count, T item, IComparer<T>? comparer)
    {
        Range(index, count); comparer ??= Comparer<T>.Default;
        int low = index, high = index + count - 1;
        try
        {
            while (low <= high)
            {
                int middle = low + (high - low) / 2; int order = comparer.Compare(_items[middle], item);
                if (order == 0) return middle; if (order < 0) low = middle + 1; else high = middle - 1;
            }
        }
        catch (Exception) { throw new InvalidOperationException("Failed to compare two elements in the array."); }
        return ~low;
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
