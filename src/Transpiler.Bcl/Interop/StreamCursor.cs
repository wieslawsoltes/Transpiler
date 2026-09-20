using System;
using Transpiler.Bcl.Async;
using Transpiler.Bcl.Tasks;
using Transpiler.Bcl.Threading;

namespace Transpiler.Bcl.Interop;

/// <summary>
/// Owns an enumerator, its cancellation source and exactly one outstanding operation.
/// Host adapters may poll, stop waiting, and resume without reissuing or re-consuming a ValueTask.
/// All lifetime/consumption algorithms in this class are compiled as ordinary managed IL.
/// </summary>
public sealed class StreamCursor<T>
{
    private IAsyncEnumerator<T>? _enumerator;
    private IStreamFactory<T>? _factory;
    private CancellationTokenSource? _cancellation;
    private ValueTask<bool> _move;
    private ValueTask _dispose;
    private bool _moving;
    private bool _disposing;
    private bool _closed;
    private bool _ended;
    private bool _hasCurrent;

    private StreamCursor(IAsyncEnumerator<T> enumerator, CancellationTokenSource cancellation)
    { _enumerator = enumerator; _cancellation = cancellation; }

    public static StreamCursor<T> Open(IAsyncEnumerable<T> enumerable)
    {
        if (enumerable == null) throw new ArgumentNullException(nameof(enumerable));
        var cancellation = new CancellationTokenSource();
        try
        {
            var enumerator = enumerable.GetAsyncEnumerator(cancellation.Token);
            if (enumerator == null) throw new InvalidOperationException("GetAsyncEnumerator returned null.");
            return new StreamCursor<T>(enumerator, cancellation);
        }
        catch { cancellation.Dispose(); throw; }
    }

    private StreamCursor(IStreamFactory<T> factory)
    { _factory = factory; _cancellation = new CancellationTokenSource(); }

    public static StreamCursor<T> FromFactory(IStreamFactory<T> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        return new StreamCursor<T>(factory);
    }

    public bool FactoryPending => _factory != null;
    public bool FactoryCompleted => _factory != null && _factory.IsCompleted;

    public void FinishFactory()
    {
        var factory = _factory;
        if (factory == null || !factory.IsCompleted) throw new InvalidOperationException("No completed factory is available.");
        _factory = null;
        try
        {
            var enumerable = factory.Finish();
            if (enumerable == null) throw new ArgumentNullException(nameof(enumerable));
            _enumerator = enumerable.GetAsyncEnumerator(_cancellation!.Token);
            if (_enumerator == null) throw new InvalidOperationException("GetAsyncEnumerator returned null.");
        }
        catch { Release(); throw; }
    }

    public bool IsClosed => _closed;
    public bool MovePending => _moving;
    public bool MoveCompleted => _moving && _move.IsCompleted;
    public bool DisposePending => _disposing;
    public bool DisposeCompleted => _closed || _disposing && _dispose.IsCompleted;
    public T Current => _hasCurrent ? _enumerator!.Current : throw new InvalidOperationException("No current stream value.");

    public void StartMove()
    {
        if (_closed || _ended || _moving || _disposing || _factory != null) throw new InvalidOperationException("The cursor cannot start another move.");
        _hasCurrent = false;
        try { _move = _enumerator!.MoveNextAsync(); _moving = true; }
        catch { _ended = true; throw; }
    }

    public bool FinishMove()
    {
        if (!_moving || !_move.IsCompleted) throw new InvalidOperationException("No completed move is available.");
        var operation = _move;
        _move = default; _moving = false;
        try
        {
            _hasCurrent = operation.GetAwaiter().GetResult();
            _ended = !_hasCurrent;
            return _hasCurrent;
        }
        catch { _ended = true; throw; }
    }

    public void Cancel() { _cancellation?.Cancel(); }

    public void StartDispose()
    {
        if (_closed || _disposing) return;
        if (_moving || _factory != null) throw new InvalidOperationException("Consume the outstanding factory/move before disposal.");
        _hasCurrent = false; _ended = true; _disposing = true;
        try { _dispose = _enumerator!.DisposeAsync(); }
        catch { Release(); throw; }
    }

    public void FinishDispose()
    {
        if (_closed) return;
        if (!_disposing || !_dispose.IsCompleted) throw new InvalidOperationException("No completed disposal is available.");
        var operation = _dispose;
        _dispose = default;
        try { operation.GetAwaiter().GetResult(); }
        finally { Release(); }
    }

    private void Release()
    {
        var cancellation = _cancellation;
        _enumerator = null; _factory = null; _cancellation = null; _move = default; _dispose = default;
        _hasCurrent = false; _moving = false; _disposing = false; _closed = true;
        cancellation?.Dispose();
    }
}
