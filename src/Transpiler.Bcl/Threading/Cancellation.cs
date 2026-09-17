using System;
using System.Runtime.CompilerServices;
using Transpiler.Bcl.Tasks;

namespace Transpiler.Bcl.Threading;

/// <summary>Single-thread cooperative token; no WaitHandle or execution-context capture.</summary>
public readonly struct CancellationToken : IEquatable<CancellationToken>
{
    internal readonly CancellationTokenSource? Source;
    internal CancellationToken(CancellationTokenSource source) { Source = source; }
    public CancellationToken(bool canceled) { Source = canceled ? CancellationTokenSource.CanceledSource : null; }
    public static CancellationToken None => default;
    public bool CanBeCanceled => Source != null;
    public bool IsCancellationRequested => Source != null && Source.IsCancellationRequested;
    public void ThrowIfCancellationRequested() { if (IsCancellationRequested) throw new OperationCanceledException(this); }
    public CancellationTokenRegistration Register(Action callback)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        return Source == null ? default : Source.Register(callback);
    }
    public CancellationTokenRegistration Register(Action<object?> callback, object? state)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        return Register(() => callback(state));
    }
    public CancellationTokenRegistration Register(Action<object?, CancellationToken> callback, object? state)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        var token = this;
        return Register(() => callback(state, token));
    }
    public CancellationTokenRegistration UnsafeRegister(Action<object?> callback, object? state) => Register(callback, state);
    public CancellationTokenRegistration UnsafeRegister(Action<object?, CancellationToken> callback, object? state) => Register(callback, state);
    public bool Equals(CancellationToken other) => object.ReferenceEquals(Source, other.Source);
    public override bool Equals(object? other) => other is CancellationToken token && Equals(token);
    public override int GetHashCode() => Source == null ? 0 : RuntimeHelpers.GetHashCode(Source);
    public static bool operator ==(CancellationToken left, CancellationToken right) => left.Equals(right);
    public static bool operator !=(CancellationToken left, CancellationToken right) => !left.Equals(right);
}

public readonly struct CancellationTokenRegistration : IDisposable, IEquatable<CancellationTokenRegistration>
{
    private readonly CancellationTokenSource.Callback? _callback;
    internal CancellationTokenRegistration(CancellationTokenSource.Callback callback) { _callback = callback; }
    public CancellationToken Token => _callback == null ? default : _callback.Token;
    public bool Unregister() => _callback != null && _callback.Unregister();
    public void Dispose() { Unregister(); }
    public Tasks.ValueTask DisposeAsync() { Dispose(); return default; }
    public bool Equals(CancellationTokenRegistration other) => object.ReferenceEquals(_callback, other._callback);
    public override bool Equals(object? other) => other is CancellationTokenRegistration registration && Equals(registration);
    public override int GetHashCode() => _callback == null ? 0 : RuntimeHelpers.GetHashCode(_callback);
    public static bool operator ==(CancellationTokenRegistration left, CancellationTokenRegistration right) => left.Equals(right);
    public static bool operator !=(CancellationTokenRegistration left, CancellationTokenRegistration right) => !left.Equals(right);
}

/// <summary>Reentrant LIFO callbacks. Disposing linked sources detaches parent subscriptions.</summary>
public class CancellationTokenSource : IDisposable
{
    internal static readonly CancellationTokenSource CanceledSource = new CancellationTokenSource(true);
    private int _state; // 0 live, 1 notifying, 2 notified
    private bool _disposed;
    private CallbackList? _callbacks;
    private CancellationTokenRegistration[]? _links;
    private HostTimer? _timer;
    public CancellationTokenSource() { }
    public CancellationTokenSource(int millisecondsDelay)
    {
        if (millisecondsDelay < -1) throw new ArgumentOutOfRangeException(nameof(millisecondsDelay));
        if (millisecondsDelay == 0) _state = 2;
        else if (millisecondsDelay != -1) _timer = new HostTimer(millisecondsDelay, CancelFromTimer);
    }
    private CancellationTokenSource(bool canceled) { _state = canceled ? 2 : 0; }
    public bool IsCancellationRequested => _state != 0;
    public CancellationToken Token { get { ThrowIfDisposed(); return new CancellationToken(this); } }
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException("CancellationTokenSource"); }
    private void DisposeTimer() { var timer = _timer; _timer = null; timer?.Dispose(); }
    private void CancelFromTimer() { if (!_disposed) Cancel(); }
    public void CancelAfter(int millisecondsDelay)
    {
        // Argument validation precedes disposal and cancellation checks, as on CoreCLR.
        if (millisecondsDelay < -1) throw new ArgumentOutOfRangeException(nameof(millisecondsDelay));
        ThrowIfDisposed();
        if (_state != 0) return;
        if (_timer != null) _timer.Change(millisecondsDelay);
        else if (millisecondsDelay != -1) _timer = new HostTimer(millisecondsDelay, CancelFromTimer);
    }
    internal sealed class CallbackList
    {
        internal Callback? Head;
        internal void Clear() { while (Head != null) Head.Unregister(); }
    }
    internal sealed class Callback
    {
        internal readonly CancellationToken Token;
        internal CallbackList? List;
        internal Callback? Previous;
        internal Callback? Next;
        internal Action? Action;
        internal Callback(CancellationToken token, CallbackList list, Action action)
        { Token = token; List = list; Action = action; Next = list.Head; if (Next != null) Next.Previous = this; list.Head = this; }
        internal bool Unregister()
        {
            if (List == null) return false;
            if (Previous == null) List.Head = Next; else Previous.Next = Next;
            if (Next != null) Next.Previous = Previous;
            List = null; Previous = null; Next = null; Action = null; return true;
        }
    }
    internal CancellationTokenRegistration Register(Action action)
    {
        if (IsCancellationRequested) { action(); return default; }
        if (_disposed) return default;
        _callbacks ??= new CallbackList();
        return new CancellationTokenRegistration(new Callback(new CancellationToken(this), _callbacks, action));
    }
    public void Cancel() => Cancel(false);
    public void Cancel(bool throwOnFirstException)
    {
        ThrowIfDisposed();
        if (_state != 0) return;
        _state = 1;
        DisposeTimer();
        CallbackList? list = _callbacks;
        _callbacks = null; // Local list survives Dispose from inside a callback.
        var errors = new List<Exception>();
        try
        {
            while (list?.Head != null)
            {
                Callback callback = list.Head;
                Action action = callback.Action!;
                callback.Unregister();
                try { action(); }
                catch (Exception error) { if (throwOnFirstException) throw; errors.Add(error); }
            }
        }
        finally { _state = 2; list?.Clear(); }
        if (errors.Count != 0) throw new AggregateException(errors);
    }
    public bool TryReset()
    {
        ThrowIfDisposed();
        if (_state != 0) return false;
        if (_timer != null)
        {
            // Disarming cannot retract an already-queued timer callback.
            if (!_timer.TryReset()) return false;
            _timer = null;
        }
        _callbacks?.Clear(); _callbacks = null; return true;
    }
    public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken token) => CreateLinkedTokenSource(new[] { token });
    public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken first, CancellationToken second)
        => CreateLinkedTokenSource(new[] { first, second });
    public static CancellationTokenSource CreateLinkedTokenSource(params CancellationToken[] tokens)
    {
        if (tokens == null) throw new ArgumentNullException(nameof(tokens));
        if (tokens.Length == 0) throw new ArgumentException("At least one token is required.");
        var source = new CancellationTokenSource();
        var links = new CancellationTokenRegistration[tokens.Length];
        source._links = links;
        for (int i = 0; i < tokens.Length; i++) links[i] = tokens[i].Register(source.Cancel);
        return source;
    }
    public void Dispose() { Dispose(true); }
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;
        DisposeTimer();
        _callbacks?.Clear(); _callbacks = null;
        var links = _links; _links = null;
        if (links != null) for (int i = 0; i < links.Length; i++) links[i].Dispose();
    }
}
