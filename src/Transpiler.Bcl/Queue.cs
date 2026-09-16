using System;
using System.Collections;
using System.Collections.Generic;
namespace Transpiler.Bcl;

/// <summary>Managed circular buffer; no target-specific collection algorithm.</summary>
public class Queue<T> : IEnumerable<T>
{
    private T[] _items;
    private int _head, _count, _version;
    public Queue() : this(0) { }
    public Queue(int capacity)
    { if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity)); _items = new T[capacity]; }
    public Queue(IEnumerable<T> values) : this()
    { if (values == null) throw new ArgumentNullException(nameof(values)); foreach (var item in values) Enqueue(item); }
    public int Count => _count;
    public void Enqueue(T value)
    {
        if (_count == _items.Length)
        {
            int capacity = _items.Length == 0 ? 4 : checked(_items.Length * 2);
            var next = new T[capacity];
            for (int i = 0; i < _count; i++) next[i] = _items[(_head + i) % _items.Length];
            _items = next; _head = 0;
        }
        _items[(_head + _count) % _items.Length] = value; _count++; _version++;
    }
    public T Peek()
    { if (_count == 0) throw new InvalidOperationException("Queue empty."); return _items[_head]; }
    public T Dequeue()
    {
        T result = Peek(); _items[_head] = default!;
        _head = (_head + 1) % _items.Length; _count--; _version++; return result;
    }
    public bool TryDequeue(out T result)
    { if (_count == 0) { result = default!; return false; } result = Dequeue(); return true; }
    public bool TryPeek(out T result)
    { if (_count == 0) { result = default!; return false; } result = Peek(); return true; }
    public void Clear()
    { for (int i = 0; i < _items.Length; i++) _items[i] = default!; _head = 0; _count = 0; _version++; }
    public T[] ToArray()
    { var result = new T[_count]; for (int i = 0; i < _count; i++) result[i] = _items[(_head + i) % _items.Length]; return result; }
    public Enumerator GetEnumerator() => new Enumerator(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public struct Enumerator : IEnumerator<T>
    {
        private readonly Queue<T> _owner;
        private readonly int _version;
        private int _index;
        private T _current;
        internal Enumerator(Queue<T> owner) { _owner = owner; _version = owner._version; _index = -1; _current = default!; }
        public T Current
        { get { if (_index < 0) throw new InvalidOperationException("Invalid enumeration state."); return _current; } }
        object? IEnumerator.Current => Current;
        public bool MoveNext()
        {
            if (_version != _owner._version) throw new InvalidOperationException("Collection was modified.");
            if (_index == -2) return false;
            _index++;
            if (_index < _owner._count) { _current = _owner._items[(_owner._head + _index) % _owner._items.Length]; return true; }
            _index = -2; _current = default!; return false;
        }
        void IEnumerator.Reset()
        { if (_version != _owner._version) throw new InvalidOperationException("Collection was modified."); _index = -1; _current = default!; }
        public void Dispose() { _index = -2; _current = default!; }
    }
}
