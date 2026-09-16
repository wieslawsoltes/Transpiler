using System;
using System.Collections.Generic;
namespace Transpiler.Bcl;

/// <summary>Original portable LINQ subset. Iterator state machines are Roslyn-generated and compiled as ordinary CIL.</summary>
public static partial class Enumerable
{
    public static IEnumerable<int> Range(int start, int count)
    {
        if (count < 0 || (long)start + count - 1 > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(count));
        return RangeIterator(start, count);
    }
    private static IEnumerable<int> RangeIterator(int start, int count)
    { for (int i = 0; i < count; i++) yield return unchecked(start + i); }
    public static IEnumerable<T> Repeat<T>(T value, int count)
    { if (count < 0) throw new ArgumentOutOfRangeException(nameof(count)); return RepeatIterator(value, count); }
    private static IEnumerable<T> RepeatIterator<T>(T value, int count)
    { for (int i = 0; i < count; i++) yield return value; }
    public static IEnumerable<T> Where<T>(IEnumerable<T> source, Func<T, bool> predicate)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return WhereIterator(source, predicate);
    }
    private static IEnumerable<T> WhereIterator<T>(IEnumerable<T> source, Func<T, bool> predicate)
    { foreach (T item in source) if (predicate(item)) yield return item; }
    public static IEnumerable<R> Select<T, R>(IEnumerable<T> source, Func<T, R> selector)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (selector == null) throw new ArgumentNullException(nameof(selector));
        return SelectIterator(source, selector);
    }
    private static IEnumerable<R> SelectIterator<T, R>(IEnumerable<T> source, Func<T, R> selector)
    { foreach (T item in source) yield return selector(item); }
    public static IEnumerable<T> Take<T>(IEnumerable<T> source, int count)
    { if (source == null) throw new ArgumentNullException(nameof(source)); return TakeIterator(source, count); }
    private static IEnumerable<T> TakeIterator<T>(IEnumerable<T> source, int count)
    {
        if (count <= 0) yield break;
        foreach (T item in source) { yield return item; if (--count == 0) yield break; }
    }
    public static IEnumerable<T> Skip<T>(IEnumerable<T> source, int count)
    { if (source == null) throw new ArgumentNullException(nameof(source)); return SkipIterator(source, count); }
    private static IEnumerable<T> SkipIterator<T>(IEnumerable<T> source, int count)
    { foreach (T item in source) { if (count > 0) count--; else yield return item; } }
    public static int Count<T>(IEnumerable<T> source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        int n = 0; foreach (T item in source) n = checked(n + 1); return n;
    }
    public static bool Any<T>(IEnumerable<T> source)
    { if (source == null) throw new ArgumentNullException(nameof(source)); using var e = source.GetEnumerator(); return e.MoveNext(); }
    public static bool Any<T>(IEnumerable<T> source, Func<T, bool> predicate)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        foreach (T item in source) if (predicate(item)) return true; return false;
    }
    public static bool All<T>(IEnumerable<T> source, Func<T, bool> predicate)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        foreach (T item in source) if (!predicate(item)) return false; return true;
    }
    public static T First<T>(IEnumerable<T> source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        using var e = source.GetEnumerator();
        if (!e.MoveNext()) throw new InvalidOperationException("Sequence contains no elements"); return e.Current;
    }
    public static T FirstOrDefault<T>(IEnumerable<T> source)
    { if (source == null) throw new ArgumentNullException(nameof(source)); using var e = source.GetEnumerator(); return e.MoveNext() ? e.Current : default!; }
    public static R Aggregate<T, R>(IEnumerable<T> source, R seed, Func<R, T, R> operation)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        foreach (T item in source) seed = operation(seed, item); return seed;
    }
    public static int Sum(IEnumerable<int> source)
    { if (source == null) throw new ArgumentNullException(nameof(source)); int sum = 0; foreach (int item in source) sum = checked(sum + item); return sum; }
    public static long Sum(IEnumerable<long> source)
    { if (source == null) throw new ArgumentNullException(nameof(source)); long sum = 0; foreach (long item in source) sum = checked(sum + item); return sum; }
    public static double Sum(IEnumerable<double> source)
    { if (source == null) throw new ArgumentNullException(nameof(source)); double sum = 0; foreach (double item in source) sum += item; return sum; }
    public static T[] ToArray<T>(IEnumerable<T> source) => new List<T>(source).ToArray();
    public static System.Collections.Generic.List<T> ToList<T>(IEnumerable<T> source) => new System.Collections.Generic.List<T>(source);
}
