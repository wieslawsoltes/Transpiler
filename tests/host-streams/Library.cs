using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

public struct Item { public int X; }
public sealed class Probe : IAsyncEnumerable<int>, IAsyncEnumerator<int>, IValueTaskSource<bool>, IValueTaskSource
{
    public readonly int Mode;
    public int Acquired, Moves, MoveReads, Disposes, DisposeReads, Canceled;
    public bool InMove;
    private int _index;
    private CancellationTokenRegistration _registration;
    private ManualResetValueTaskSourceCore<bool> _move;
    private ManualResetValueTaskSourceCore<bool> _dispose;
    public Probe(int mode) { Mode = mode; }
    public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        Acquired++;
        if (Mode == 10) throw new InvalidOperationException("acquire fault");
        if (Mode == 11) return null;
        _registration = cancellationToken.Register(() =>
        {
            Canceled++;
            if (Mode == 2 && InMove) _move.SetException(new OperationCanceledException(cancellationToken));
        });
        return this;
    }
    public int Current => Mode == 3 ? throw new InvalidOperationException("current fault") : _index;
    public ValueTask<bool> MoveNextAsync()
    {
        Moves++;
        if (InMove) throw new Exception("overlapping moves");
        if (Mode == 4) throw new InvalidOperationException("move call fault");
        _move.Reset(); InMove = true; _index++;
        if (Mode == 5) _move.SetException(new InvalidOperationException("move result fault"));
        else if (Mode != 1 && Mode != 2) _move.SetResult(_index <= 2);
        return new ValueTask<bool>(this, _move.Version);
    }
    public void CompleteMove(bool result) => _move.SetResult(result);
    public ValueTask DisposeAsync()
    {
        if (InMove) throw new Exception("dispose overlaps move");
        Disposes++;
        if (Disposes != 1) throw new Exception("duplicate disposal");
        _registration.Dispose();
        if (Mode == 6) throw new InvalidOperationException("dispose call fault");
        _dispose.Reset();
        if (Mode == 7) _dispose.SetException(new InvalidOperationException("dispose result fault"));
        else if (Mode != 8) _dispose.SetResult(true);
        return new ValueTask(this, _dispose.Version);
    }
    public void CompleteDispose() => _dispose.SetResult(true);
    public ValueTaskSourceStatus GetStatus(short token) => _move.GetStatus(token);
    public bool GetResult(short token)
    { MoveReads++; InMove = false; return _move.GetResult(token); }
    public void OnCompleted(Action<object> callback, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _move.OnCompleted(callback, state, token, flags);
    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _dispose.GetStatus(token);
    void IValueTaskSource.GetResult(short token) { DisposeReads++; _dispose.GetResult(token); }
    void IValueTaskSource.OnCompleted(Action<object> callback, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _dispose.OnCompleted(callback, state, token, flags);
}
public static class Kernel
{
    private static Probe _probe;
    private static int _factories, _closed;
    public static IAsyncEnumerable<int> Probe(int mode) { _factories++; return _probe = new Probe(mode); }
    public static int Factories() => _factories;
    public static int Closed() => _closed;
    public static int Acquired() => _probe.Acquired;
    public static int Moves() => _probe.Moves;
    public static int Reads() => _probe.MoveReads;
    public static int Disposes() => _probe.Disposes;
    public static int DisposeReads() => _probe.DisposeReads;
    public static int Canceled() => _probe.Canceled;
    public static void CompleteMove(bool value) => _probe.CompleteMove(value);
    public static void CompleteDispose() => _probe.CompleteDispose();
    public static IAsyncEnumerable<int> Null() => null;
    public static IAsyncEnumerable<int> FactoryFault() => throw new InvalidOperationException("factory fault");
    public static IAsyncEnumerable<long> Numbers(int count) { _factories++; return Generate(count); }
    private static async IAsyncEnumerable<long> Generate(int count, [EnumeratorCancellation] CancellationToken token = default)
    {
        try { for (int i = 0; i < count; i++) { await Task.Yield(); token.ThrowIfCancellationRequested(); yield return 9007199254740993L + i; } }
        finally { await Task.Yield(); _closed++; }
    }
    public static async IAsyncEnumerable<string> Text() { await Task.Yield(); yield return "A\U0001f682B"; yield return null; }
    public static async IAsyncEnumerable<bool> Flags() { await Task.Yield(); yield return true; yield return false; }
    public static async IAsyncEnumerable<double> Floats() { await Task.Yield(); yield return double.NaN; yield return -0.0; yield return double.PositiveInfinity; }
    public static async IAsyncEnumerable<Item> Items() { var item = new Item { X = 7 }; await Task.Yield(); yield return item; yield return item; }
}
