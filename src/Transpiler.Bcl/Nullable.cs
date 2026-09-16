using System;
namespace Transpiler.Bcl;

/// <summary>Managed nullable storage/API. Boxing and type tests are separate compiler/runtime rules.</summary>
public struct Nullable<T> where T : struct
{
    private readonly bool hasValue;
    private T value;
    public Nullable(T value) { this.value = value; hasValue = true; }
    public bool HasValue => hasValue;
    public T Value => hasValue ? value : throw new InvalidOperationException("Nullable object must have a value.");
    public T GetValueOrDefault() => value;
    public T GetValueOrDefault(T fallback) => hasValue ? value : fallback;
    public override bool Equals(object? other) => hasValue ? other is T item && RuntimeValue.Equal(value, item) : other == null;
    public override int GetHashCode() => hasValue ? RuntimeValue.Hash(value) : 0;
    public override string? ToString() => hasValue ? value.ToString() : "";
}
public static class Nullable
{
    public static bool Equals<T>(Nullable<T> left, Nullable<T> right) where T : struct =>
        left.HasValue == right.HasValue && (!left.HasValue || RuntimeValue.Equal(left.GetValueOrDefault(), right.GetValueOrDefault()));
    public static int Compare<T>(Nullable<T> left, Nullable<T> right) where T : struct =>
        left.HasValue ? right.HasValue ? RuntimeValue.Compare(left.GetValueOrDefault(), right.GetValueOrDefault()) : 1 : right.HasValue ? -1 : 0;
}
