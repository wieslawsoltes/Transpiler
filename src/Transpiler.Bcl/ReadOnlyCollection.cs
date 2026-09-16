using System;
using System.Collections;
using System.Collections.Generic;

namespace Transpiler.Bcl;

/// <summary>A live, read-only generic view. No snapshot and no host-list substitution.</summary>
public class ReadOnlyCollection<T> : IList<T>, IReadOnlyList<T>
{
    private readonly IList<T> _items;
    public ReadOnlyCollection(IList<T> list)
    {
        if (list == null) throw new ArgumentNullException(nameof(list));
        _items = list is T[] array ? new FixedArrayList<T>(array) : list;
    }
    public int Count => _items.Count;
    public T this[int index] => _items[index];
    protected IList<T> Items => _items;
    public bool Contains(T value) => _items.Contains(value);
    public int IndexOf(T value) => _items.IndexOf(value);
    public void CopyTo(T[] array, int index) => _items.CopyTo(array, index);
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    bool ICollection<T>.IsReadOnly => true;
    T IList<T>.this[int index] { get => this[index]; set => throw new NotSupportedException("Collection is read-only."); }
    void ICollection<T>.Add(T value) => throw new NotSupportedException("Collection is read-only.");
    void ICollection<T>.Clear() => throw new NotSupportedException("Collection is read-only.");
    bool ICollection<T>.Remove(T value) => throw new NotSupportedException("Collection is read-only.");
    void IList<T>.Insert(int index, T value) => throw new NotSupportedException("Collection is read-only.");
    void IList<T>.RemoveAt(int index) => throw new NotSupportedException("Collection is read-only.");
}

// Preserve array aliasing without requiring target-native arrays to implement the entire IList<T> ABI.
internal sealed class FixedArrayList<T> : IList<T>
{
    private readonly T[] _array;
    internal FixedArrayList(T[] array) { _array = array; }
    public int Count => _array.Length;
    public bool IsReadOnly => true;
    public T this[int index]
    {
        get { if ((uint)index >= (uint)_array.Length) throw new ArgumentOutOfRangeException(nameof(index)); return _array[index]; }
        set { if ((uint)index >= (uint)_array.Length) throw new ArgumentOutOfRangeException(nameof(index)); _array[index] = value; }
    }
    public int IndexOf(T value)
    { for (int i = 0; i < _array.Length; i++) if (EqualityComparer<T>.Default.Equals(_array[i], value)) return i; return -1; }
    public bool Contains(T value) => IndexOf(value) >= 0;
    public void CopyTo(T[] array, int index)
    {
        if (array == null) throw new ArgumentNullException(nameof(array));
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (index > array.Length || _array.Length > array.Length - index) throw new ArgumentException("Destination array is too small.");
        for (int i = 0; i < _array.Length; i++) array[index + i] = _array[i];
    }
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_array).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Add(T value) => throw new NotSupportedException("Collection has fixed size.");
    public void Clear() => throw new NotSupportedException("Collection has fixed size.");
    public bool Remove(T value) => throw new NotSupportedException("Collection has fixed size.");
    public void Insert(int index, T value) => throw new NotSupportedException("Collection has fixed size.");
    public void RemoveAt(int index) => throw new NotSupportedException("Collection has fixed size.");
}
