using System;
using System.Collections;
using System.Collections.Generic;

namespace Transpiler.Bcl;

/// <summary>Managed set algorithms over the shared hash-table storage, with a separate null-element policy.</summary>
public class HashSet<T> : ISet<T>, IReadOnlySet<T>
{
    private readonly Dictionary<T, byte> _items;
    public HashSet() : this(0, null) { }
    public HashSet(int capacity) : this(capacity, null) { }
    public HashSet(IEqualityComparer<T>? comparer) : this(0, comparer) { }
    public HashSet(int capacity, IEqualityComparer<T>? comparer) { _items = new Dictionary<T, byte>(capacity, comparer, true); }
    public HashSet(IEnumerable<T> collection) : this(collection, null) { }
    public HashSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer) : this(0, comparer)
    { UnionWith(collection); }
    public int Count => _items.Count;
    public int Capacity => _items.Capacity;
    public IEqualityComparer<T> Comparer => _items.Comparer;
    bool ICollection<T>.IsReadOnly => false;
    public bool Add(T item) => _items.TryAdd(item, 0);
    void ICollection<T>.Add(T item) => Add(item);
    public bool Remove(T item) => _items.Remove(item);
    public bool Contains(T item) => _items.ContainsKey(item);
    public bool TryGetValue(T equalValue, out T actualValue) => _items.TryGetStoredKey(equalValue, out actualValue);
    public void Clear() => _items.Clear();
    public int EnsureCapacity(int capacity) => _items.EnsureCapacity(capacity);
    public void TrimExcess() => _items.TrimExcess();
    public void TrimExcess(int capacity) => _items.TrimExcess(capacity);
    public int RemoveWhere(Predicate<T> match)
    {
        if (match == null) throw new ArgumentNullException(nameof(match));
        int removed = 0;
        foreach (var item in this) if (match(item) && Remove(item)) removed++;
        return removed;
    }
    public void CopyTo(T[] array) => CopyTo(array, 0, Count);
    public void CopyTo(T[] array, int arrayIndex) => CopyTo(array, arrayIndex, Count);
    public void CopyTo(T[] array, int arrayIndex, int count)
    {
        if (array == null) throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (arrayIndex > array.Length || count > array.Length - arrayIndex) throw new ArgumentException("Destination array is too small.");
        // Like the framework API, count may exceed Count; copy at most the available elements.
        foreach (var item in this) { if (count-- == 0) break; array[arrayIndex++] = item; }
    }
    private static void Require(IEnumerable<T> other)
    { if (other == null) throw new ArgumentNullException(nameof(other)); }
    public void UnionWith(IEnumerable<T> other)
    { Require(other); foreach (var item in other) Add(item); }
    public void ExceptWith(IEnumerable<T> other)
    {
        Require(other);
        if (object.ReferenceEquals(this, other)) { Clear(); return; }
        if (Count == 0) return;
        foreach (var item in other) Remove(item);
    }
    public void IntersectWith(IEnumerable<T> other)
    {
        Require(other);
        if (Count == 0 || object.ReferenceEquals(this, other)) return;
        var keep = new HashSet<T>(other, Comparer);
        foreach (var item in this) if (!keep.Contains(item)) Remove(item);
    }
    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        Require(other);
        if (object.ReferenceEquals(this, other)) { Clear(); return; }
        var distinct = new HashSet<T>(other, Comparer);
        foreach (var item in distinct) if (!Remove(item)) Add(item);
    }
    public bool IsSubsetOf(IEnumerable<T> other)
    {
        Require(other);
        if (Count == 0 || object.ReferenceEquals(this, other)) return true;
        var set = new HashSet<T>(other, Comparer);
        foreach (var item in this) if (!set.Contains(item)) return false;
        return true;
    }
    public bool IsProperSubsetOf(IEnumerable<T> other)
    { Require(other); var set = new HashSet<T>(other, Comparer); return Count < set.Count && IsSubsetOf(set); }
    public bool IsSupersetOf(IEnumerable<T> other)
    { Require(other); foreach (var item in other) if (!Contains(item)) return false; return true; }
    public bool IsProperSupersetOf(IEnumerable<T> other)
    { Require(other); if (Count == 0) return false; var set = new HashSet<T>(other, Comparer); return Count > set.Count && IsSupersetOf(set); }
    public bool Overlaps(IEnumerable<T> other)
    { Require(other); if (Count == 0) return false; foreach (var item in other) if (Contains(item)) return true; return false; }
    public bool SetEquals(IEnumerable<T> other)
    { Require(other); if (object.ReferenceEquals(this, other)) return true; var set = new HashSet<T>(other, Comparer); return Count == set.Count && IsSupersetOf(set); }
    public Enumerator GetEnumerator() => new Enumerator(_items);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public struct Enumerator : IEnumerator<T>
    {
        private Dictionary<T, byte>.Enumerator _inner;
        internal Enumerator(Dictionary<T, byte> dictionary) { _inner = dictionary.GetEnumerator(); }
        public T Current => _inner.Current.Key;
        object? IEnumerator.Current { get { var ignored = ((IEnumerator)_inner).Current; return Current; } }
        public bool MoveNext() => _inner.MoveNext();
        public void Dispose() { }
        void IEnumerator.Reset() { var box = (IEnumerator)_inner; box.Reset(); _inner = (Dictionary<T, byte>.Enumerator)box; }
    }
}
