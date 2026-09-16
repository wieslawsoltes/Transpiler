using System;
using System.Collections.Generic;

namespace Transpiler.Bcl;

/// <summary>Managed oracle bodies for native tests; the compiler lowers these exact hooks to typed runtime operations.</summary>
public static class RuntimeValue
{
    public static bool Equal<T>(T left, T right) => global::System.Collections.Generic.EqualityComparer<T>.Default.Equals(left, right);
    public static int Hash<T>(T value) => value is null ? 0 : global::System.Collections.Generic.EqualityComparer<T>.Default.GetHashCode(value);
    public static int Compare<T>(T left, T right) => global::System.Collections.Generic.Comparer<T>.Default.Compare(left, right);
}

public abstract class EqualityComparer<T> : IEqualityComparer<T>
{
    private static readonly EqualityComparer<T> Instance = new DefaultEqualityComparer();
    public static EqualityComparer<T> Default => Instance;
    public abstract bool Equals(T? x, T? y);
    public abstract int GetHashCode(T obj);
    public static EqualityComparer<T> Create(Func<T?, T?, bool> equals, Func<T, int>? getHashCode = null)
    {
        if (equals == null) throw new ArgumentNullException(nameof(equals));
        return new CallbackEqualityComparer(equals, getHashCode);
    }
    private sealed class DefaultEqualityComparer : EqualityComparer<T>
    {
        public override bool Equals(T? x, T? y) => RuntimeValue.Equal(x, y);
        public override int GetHashCode(T obj) => RuntimeValue.Hash(obj);
    }
    private sealed class CallbackEqualityComparer : EqualityComparer<T>
    {
        private readonly Func<T?, T?, bool> _equals;
        private readonly Func<T, int>? _hash;
        public CallbackEqualityComparer(Func<T?, T?, bool> equals, Func<T, int>? hash) { _equals = equals; _hash = hash; }
        public override bool Equals(T? x, T? y) => _equals(x, y);
        public override int GetHashCode(T obj) => _hash == null ? throw new NotSupportedException("No hash function was supplied.") : _hash(obj);
    }
}

public abstract class Comparer<T> : IComparer<T>
{
    private static readonly Comparer<T> Instance = new DefaultComparer();
    public static Comparer<T> Default => Instance;
    public abstract int Compare(T? x, T? y);
    public static Comparer<T> Create(Comparison<T> comparison)
    {
        if (comparison == null) throw new ArgumentNullException(nameof(comparison));
        return new CallbackComparer(comparison);
    }
    private sealed class DefaultComparer : Comparer<T>
    { public override int Compare(T? x, T? y) => RuntimeValue.Compare(x, y); }
    private sealed class CallbackComparer : Comparer<T>
    {
        private readonly Comparison<T> _comparison;
        public CallbackComparer(Comparison<T> comparison) { _comparison = comparison; }
        public override int Compare(T? x, T? y) => _comparison(x!, y!);
    }
}

/// <summary>Ordinal code-unit ordering. Culture-specific and ignore-case factories are not substituted.</summary>
public abstract class StringComparer : IEqualityComparer<string>, IComparer<string>
{
    private static readonly StringComparer OrdinalInstance = new OrdinalComparer();
    public static StringComparer Ordinal => OrdinalInstance;
    public abstract int Compare(string? x, string? y);
    public abstract bool Equals(string? x, string? y);
    public abstract int GetHashCode(string value);
    private sealed class OrdinalComparer : StringComparer
    {
        public override int Compare(string? x, string? y) => string.CompareOrdinal(x, y);
        public override bool Equals(string? x, string? y) => string.Equals(x, y);
        public override int GetHashCode(string value)
        { if (value == null) throw new ArgumentNullException(nameof(value)); return value.GetHashCode(); }
    }
}
