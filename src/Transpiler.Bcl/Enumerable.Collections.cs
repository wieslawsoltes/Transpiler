using System;
using System.Collections.Generic;
namespace Transpiler.Bcl;

public static partial class Enumerable
{
    private static void Require<T>(IEnumerable<T> source)
    { if (source == null) throw new ArgumentNullException(nameof(source)); }
    public static IEnumerable<T> Empty<T>() => EmptyStorage<T>.Items;
    private static class EmptyStorage<T> { internal static readonly T[] Items = new T[0]; }
    public static IEnumerable<T> Concat<T>(IEnumerable<T> first, IEnumerable<T> second)
    { Require(first); Require(second); return ConcatIterator(first, second); }
    private static IEnumerable<T> ConcatIterator<T>(IEnumerable<T> first, IEnumerable<T> second)
    { foreach (var value in first) yield return value; foreach (var value in second) yield return value; }
    public static IEnumerable<T> Append<T>(IEnumerable<T> source, T element)
    { Require(source); return AppendIterator(source, element, false); }
    public static IEnumerable<T> Prepend<T>(IEnumerable<T> source, T element)
    { Require(source); return AppendIterator(source, element, true); }
    private static IEnumerable<T> AppendIterator<T>(IEnumerable<T> source, T element, bool prepend)
    { if (prepend) yield return element; foreach (var value in source) yield return value; if (!prepend) yield return element; }
    public static IEnumerable<R> SelectMany<T, R>(IEnumerable<T> source, Func<T, IEnumerable<R>> selector)
    {
        Require(source); if (selector == null) throw new ArgumentNullException(nameof(selector));
        return SelectManyIterator(source, selector);
    }
    private static IEnumerable<R> SelectManyIterator<T, R>(IEnumerable<T> source, Func<T, IEnumerable<R>> selector)
    { foreach (var item in source) foreach (var value in selector(item)) yield return value; }
    public static IEnumerable<R> SelectMany<T, C, R>(IEnumerable<T> source, Func<T, IEnumerable<C>> collectionSelector, Func<T, C, R> resultSelector)
    {
        Require(source); if (collectionSelector == null) throw new ArgumentNullException(nameof(collectionSelector));
        if (resultSelector == null) throw new ArgumentNullException(nameof(resultSelector));
        return SelectManyIterator(source, collectionSelector, resultSelector);
    }
    private static IEnumerable<R> SelectManyIterator<T, C, R>(IEnumerable<T> source, Func<T, IEnumerable<C>> collectionSelector, Func<T, C, R> resultSelector)
    { foreach (var item in source) foreach (var value in collectionSelector(item)) yield return resultSelector(item, value); }
    public static IEnumerable<T> Distinct<T>(IEnumerable<T> source) => Distinct(source, null);
    public static IEnumerable<T> Distinct<T>(IEnumerable<T> source, IEqualityComparer<T>? comparer)
    { Require(source); return DistinctIterator(source, comparer); }
    private static IEnumerable<T> DistinctIterator<T>(IEnumerable<T> source, IEqualityComparer<T>? comparer)
    { var seen = new HashSet<T>(comparer); foreach (var item in source) if (seen.Add(item)) yield return item; }
    public static IEnumerable<T> Union<T>(IEnumerable<T> first, IEnumerable<T> second) => Union(first, second, null);
    public static IEnumerable<T> Union<T>(IEnumerable<T> first, IEnumerable<T> second, IEqualityComparer<T>? comparer)
    { Require(first); Require(second); return DistinctIterator(ConcatIterator(first, second), comparer); }
    public static IEnumerable<T> Intersect<T>(IEnumerable<T> first, IEnumerable<T> second) => Intersect(first, second, null);
    public static IEnumerable<T> Intersect<T>(IEnumerable<T> first, IEnumerable<T> second, IEqualityComparer<T>? comparer)
    { Require(first); Require(second); return SetIterator(first, second, comparer, true); }
    public static IEnumerable<T> Except<T>(IEnumerable<T> first, IEnumerable<T> second) => Except(first, second, null);
    public static IEnumerable<T> Except<T>(IEnumerable<T> first, IEnumerable<T> second, IEqualityComparer<T>? comparer)
    { Require(first); Require(second); return SetIterator(first, second, comparer, false); }
    private static IEnumerable<T> SetIterator<T>(IEnumerable<T> first, IEnumerable<T> second, IEqualityComparer<T>? comparer, bool intersect)
    {
        var set = new HashSet<T>(second, comparer);
        foreach (var item in first) if (intersect ? set.Remove(item) : set.Add(item)) yield return item;
    }
    public static global::System.Collections.Generic.HashSet<T> ToHashSet<T>(IEnumerable<T> source) => ToHashSet(source, null);
    public static global::System.Collections.Generic.HashSet<T> ToHashSet<T>(IEnumerable<T> source, IEqualityComparer<T>? comparer)
    { Require(source); return new global::System.Collections.Generic.HashSet<T>(source, comparer); }
    public static global::System.Collections.Generic.Dictionary<K, T> ToDictionary<T, K>(IEnumerable<T> source, Func<T, K> keySelector) where K : notnull => ToDictionary(source, keySelector, null);
    public static global::System.Collections.Generic.Dictionary<K, T> ToDictionary<T, K>(IEnumerable<T> source, Func<T, K> keySelector, IEqualityComparer<K>? comparer) where K : notnull
    {
        Require(source); if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        var result = new global::System.Collections.Generic.Dictionary<K, T>(comparer);
        foreach (var item in source) result.Add(keySelector(item), item); return result;
    }
    public static global::System.Collections.Generic.Dictionary<K, V> ToDictionary<T, K, V>(IEnumerable<T> source, Func<T, K> keySelector, Func<T, V> elementSelector) where K : notnull => ToDictionary(source, keySelector, elementSelector, null);
    public static global::System.Collections.Generic.Dictionary<K, V> ToDictionary<T, K, V>(IEnumerable<T> source, Func<T, K> keySelector, Func<T, V> elementSelector, IEqualityComparer<K>? comparer) where K : notnull
    {
        Require(source); if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        if (elementSelector == null) throw new ArgumentNullException(nameof(elementSelector));
        var result = new global::System.Collections.Generic.Dictionary<K, V>(comparer);
        foreach (var item in source) result.Add(keySelector(item), elementSelector(item)); return result;
    }
    public static bool Contains<T>(IEnumerable<T> source, T value)
    { Require(source); if (source is ICollection<T> collection) return collection.Contains(value); return Contains(source, value, null); }
    public static bool Contains<T>(IEnumerable<T> source, T value, IEqualityComparer<T>? comparer)
    { Require(source); comparer ??= EqualityComparer<T>.Default; foreach (var item in source) if (comparer.Equals(item, value)) return true; return false; }
    public static bool SequenceEqual<T>(IEnumerable<T> first, IEnumerable<T> second) => SequenceEqual(first, second, null);
    public static bool SequenceEqual<T>(IEnumerable<T> first, IEnumerable<T> second, IEqualityComparer<T>? comparer)
    {
        Require(first); Require(second); comparer ??= EqualityComparer<T>.Default;
        using var a = first.GetEnumerator(); using var b = second.GetEnumerator();
        while (a.MoveNext()) if (!b.MoveNext() || !comparer.Equals(a.Current, b.Current)) return false;
        return !b.MoveNext();
    }
    public static IEnumerable<T> DefaultIfEmpty<T>(IEnumerable<T> source) => DefaultIfEmpty(source, default!);
    public static IEnumerable<T> DefaultIfEmpty<T>(IEnumerable<T> source, T defaultValue)
    { Require(source); return DefaultIterator(source, defaultValue); }
    private static IEnumerable<T> DefaultIterator<T>(IEnumerable<T> source, T defaultValue)
    { using var e = source.GetEnumerator(); if (!e.MoveNext()) yield return defaultValue; else do { yield return e.Current; } while (e.MoveNext()); }
    public static IEnumerable<T> Reverse<T>(IEnumerable<T> source)
    { Require(source); return ReverseIterator(source); }
    private static IEnumerable<T> ReverseIterator<T>(IEnumerable<T> source)
    { var items = new List<T>(source); for (int i = items.Count - 1; i >= 0; i--) yield return items[i]; }
    public static T Single<T>(IEnumerable<T> source)
    {
        Require(source); using var e = source.GetEnumerator(); if (!e.MoveNext()) throw new InvalidOperationException("Sequence contains no elements");
        T value = e.Current; if (e.MoveNext()) throw new InvalidOperationException("Sequence contains more than one element"); return value;
    }
    public static T? SingleOrDefault<T>(IEnumerable<T> source)
    {
        Require(source); using var e = source.GetEnumerator(); if (!e.MoveNext()) return default;
        T value = e.Current; if (e.MoveNext()) throw new InvalidOperationException("Sequence contains more than one element"); return value;
    }
    public static T Last<T>(IEnumerable<T> source)
    {
        Require(source); using var e = source.GetEnumerator(); if (!e.MoveNext()) throw new InvalidOperationException("Sequence contains no elements");
        T value; do { value = e.Current; } while (e.MoveNext()); return value;
    }
    public static T? LastOrDefault<T>(IEnumerable<T> source)
    { Require(source); T? value = default; foreach (var item in source) value = item; return value; }
}
