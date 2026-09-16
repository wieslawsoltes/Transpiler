using System;
using System.Collections;
using System.Collections.Generic;
namespace Transpiler.Bcl;

public class Stack<T> : IEnumerable<T>
{
    private T[] _items;
    private int _count, _version;
    public Stack() : this(0) { }
    public Stack(int capacity)
    { if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity)); _items = new T[capacity]; }
    public Stack(IEnumerable<T> values) : this()
    { if (values == null) throw new ArgumentNullException(nameof(values)); foreach (var item in values) Push(item); }
    public int Count => _count;
    public void Push(T value)
    {
        if (_count == _items.Length)
        {
            var next = new T[_items.Length == 0 ? 4 : checked(_items.Length * 2)];
            for (int i = 0; i < _count; i++) next[i] = _items[i];
            _items = next;
        }
        _items[_count++] = value; _version++;
    }
    public T Peek()
    { if (_count == 0) throw new InvalidOperationException("Stack empty."); return _items[_count - 1]; }
    public T Pop()
    { T result = Peek(); _items[--_count] = default!; _version++; return result; }
    public bool TryPop(out T result)
    { if (_count == 0) { result = default!; return false; } result = Pop(); return true; }
    public bool TryPeek(out T result)
    { if (_count == 0) { result = default!; return false; } result = Peek(); return true; }
    public void Clear()
    { for (int i = 0; i < _count; i++) _items[i] = default!; _count = 0; _version++; }
    public T[] ToArray()
    { var result = new T[_count]; for (int i = 0; i < _count; i++) result[i] = _items[_count - 1 - i]; return result; }
    public Enumerator GetEnumerator() => new Enumerator(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public struct Enumerator : IEnumerator<T>
    {
        private readonly Stack<T> _owner;
        private readonly int _version;
        private int _index;
        private T _current;
        internal Enumerator(Stack<T> owner) { _owner = owner; _version = owner._version; _index = -2; _current = default!; }
        public T Current
        { get { if (_index < 0) throw new InvalidOperationException("Invalid enumeration state."); return _current; } }
        object? IEnumerator.Current => Current;
        public bool MoveNext()
        {
            if (_version != _owner._version) throw new InvalidOperationException("Collection was modified.");
            if (_index == -2) _index = _owner._count; if (_index >= 0) _index--;
            if (_index >= 0) { _current = _owner._items[_index]; return true; }
            _current = default!; return false;
        }
        void IEnumerator.Reset()
        { if (_version != _owner._version) throw new InvalidOperationException("Collection was modified."); _index = -2; _current = default!; }
        public void Dispose() { _index = -1; _current = default!; }
    }
}
