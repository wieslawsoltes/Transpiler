using System;
using System.Collections;
using System.Collections.Generic;
namespace Transpiler.Bcl;

/// <summary>Compiler-linked bridge for array IEnumerable calls. Enumeration logic remains managed IL.</summary>
public sealed class ArrayEnumerator<T> : IEnumerator<T>
{
    private readonly T[] _values;
    private int _index = -1;
    public ArrayEnumerator(T[] values) { _values = values; }
    public T Current
    {
        get
        {
            if ((uint)_index >= (uint)_values.Length) throw new InvalidOperationException("Invalid enumeration state.");
            return _values[_index];
        }
    }
    object? IEnumerator.Current => Current;
    public bool MoveNext() { if (_index < _values.Length) _index++; return _index < _values.Length; }
    public void Reset() { _index = -1; }
    public void Dispose() { }
}
