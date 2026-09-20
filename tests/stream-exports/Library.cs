using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

public static class StreamExports
{
    private static int _factories, _acquisitions, _moves, _disposals, _consumptions;
    private static bool _acquiredCanceled;
    private static TaskCompletionSource<Sequence> _pending = new();
    private static FactorySource _source = new();
    private static readonly InvalidOperationException Failure = new("factory-failure");

    public static void Reset()
    {
        _factories = _acquisitions = _moves = _disposals = _consumptions = 0;
        _acquiredCanceled = false;
        _pending = new TaskCompletionSource<Sequence>();
        _source = new FactorySource();
    }
    public static int Factories() => _factories;
    public static int Acquisitions() => _acquisitions;
    public static int Moves() => _moves;
    public static int Disposals() => _disposals;
    public static int Consumptions() => _consumptions;
    public static bool AcquiredCanceled() => _acquiredCanceled;
    public static object FailureObject() => Failure;
    public static IAsyncEnumerable<int> Direct() { _factories++; return new Sequence(10); }
    public static Sequence Concrete() { _factories++; return new Sequence(10); }
    public static Base<int> Inherited() { _factories++; return new Sequence(10); }
    public static IIntSequence Interface() { _factories++; return new InterfaceSequence(); }
    public static ValueSequence Struct() { _factories++; return new ValueSequence(10); }
    public static Task<Sequence> TaskConcrete() { _factories++; return Task.FromResult(new Sequence(10)); }
    public static Task<ValueSequence> TaskStruct() { _factories++; return Task.FromResult(new ValueSequence(10)); }
    public static ValueTask<Sequence> ValueConcrete() { _factories++; return new(new Sequence(10)); }
    public static ValueTask<ValueSequence> ValueStruct() { _factories++; return new(new ValueSequence(10)); }
    public static async Task<IAsyncEnumerable<int>> TaskInterface()
    { _factories++; await Task.Yield(); return new Sequence(10); }
    public static async ValueTask<Sequence> AsyncValue()
    { _factories++; await Task.Yield(); return new Sequence(10); }
    public static async Task<Sequence> DelayedFactory()
    { _factories++; await Task.Delay(20); return new Sequence(10); }
    public static object Erased(int kind)
    { _factories++; return kind == 1 ? new ValueSequence(10) : kind == 2 ? 42 : kind == 3 ? null! : new Sequence(10); }
    public static Task<object> TaskErased() { _factories++; return Task.FromResult<object>(new Sequence(10)); }
    public static ValueTask<object> ValueErased() { _factories++; return new((object)new ValueSequence(10)); }
    public static Multi Multiple() { _factories++; return new Multi(); }
    public static Task<Multi> TaskMultiple() { _factories++; return Task.FromResult(new Multi()); }
    public static Sequence ThrowingFactory() { _factories++; throw Failure; }
    public static Task<Sequence> FaultedFactory() { _factories++; return Task.FromException<Sequence>(Failure); }
    public static Task<Sequence> CanceledFactory() { _factories++; return Task.FromCanceled<Sequence>(new CancellationToken(true)); }
    public static Sequence NullDirect() { _factories++; return null!; }
    public static Task<Sequence> NullTask() { _factories++; return null!; }
    public static Task<Sequence> NullResult() { _factories++; return Task.FromResult<Sequence>(null!); }
    public static ValueTask<Sequence> DefaultValue() { _factories++; return default; }
    public static Task<Sequence> PendingFactory() { _factories++; return _pending.Task; }
    public static void CompleteFactory() => _pending.SetResult(new Sequence(10));
    public static void FailFactory() => _pending.SetException(Failure);
    public static ValueTask<Sequence> SourceFactory() { _factories++; return new(_source, _source.Version); }
    public static void CompleteSource() => _source.Complete();
    public static ValueTask<Sequence> CompletedSource()
    { _factories++; var value = new ValueTask<Sequence>(_source, _source.Version); _source.Complete(); return value; }
    public static Task<Sequence> BadAcquisition() { _factories++; return Task.FromResult(new Sequence(-1)); }
    public static Task<Sequence> NullEnumerator() { _factories++; return Task.FromResult(new Sequence(-2)); }
    public static Task<Sequence> FaultingDispose() { _factories++; return Task.FromResult(new Sequence(-3)); }
    public static Task<Sequence> DelayedDispose() { _factories++; return Task.FromResult(new Sequence(-4)); }
    public static Task<IAsyncEnumerable<string>> Covariant()
    { _factories++; return Task.FromResult<IAsyncEnumerable<string>>(new TextSequence()); }
    public static Task<IAsyncEnumerable<Pair>> ValueElements()
    { _factories++; return Task.FromResult<IAsyncEnumerable<Pair>>(new PairSequence()); }
    public static Task<Task<IAsyncEnumerable<int>>> NestedUnsupported()
    { _factories++; return Task.FromResult(Task.FromResult<IAsyncEnumerable<int>>(new Sequence(10))); }
    public static int NonStream() { _factories++; return 42; }
    public static Sequence Overloaded() => new(10);
    public static Sequence Overloaded(int start) => new(start);
    public static async Task<int> Pump() { await Task.Yield(); return 1; }

    public class Base<T> : IAsyncEnumerable<T>
    {
        private readonly T _first, _second;
        private readonly int _mode;
        public Base(T first, T second, int mode) { _first = first; _second = second; _mode = mode; }
        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            _acquisitions++; _acquiredCanceled = cancellationToken.IsCancellationRequested;
            if (_mode == -1) throw new InvalidOperationException("acquire-failure");
            if (_mode == -2) return null!;
            return new Enumerator<T>(_first, _second, _mode, cancellationToken);
        }
    }
    public sealed class Sequence : Base<int>
    { public Sequence(int start) : base(start < 0 ? 10 : start, start < 0 ? 11 : start + 1, start) { } }
    public interface IIntSequence : IAsyncEnumerable<int> { }
    public sealed class InterfaceSequence : Base<int>, IIntSequence
    { public InterfaceSequence() : base(10, 11, 0) { } }
    public readonly struct ValueSequence : IAsyncEnumerable<int>
    {
        private readonly int _start;
        public ValueSequence(int start) { _start = start; }
        public IAsyncEnumerator<int> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new Sequence(_start).GetAsyncEnumerator(cancellationToken);
    }
    private sealed class Enumerator<T> : IAsyncEnumerator<T>
    {
        private readonly T _first, _second;
        private readonly int _mode;
        private readonly CancellationToken _token;
        private int _position;
        public Enumerator(T first, T second, int mode, CancellationToken token)
        { _first = first; _second = second; _mode = mode; _token = token; }
        public T Current => _position == 1 ? _first : _second;
        public ValueTask<bool> MoveNextAsync()
        { _moves++; _token.ThrowIfCancellationRequested(); _position++; return new(_position <= 2); }
        public async ValueTask DisposeAsync()
        {
            if (_mode == -4) await Task.Delay(5);
            else await Task.Yield();
            _disposals++;
            if (_mode == -3) throw new InvalidOperationException("dispose-failure");
        }
    }
    public sealed class Multi : IAsyncEnumerable<int>, IAsyncEnumerable<string>
    {
        IAsyncEnumerator<int> IAsyncEnumerable<int>.GetAsyncEnumerator(CancellationToken cancellationToken)
            => new Sequence(10).GetAsyncEnumerator(cancellationToken);
        IAsyncEnumerator<string> IAsyncEnumerable<string>.GetAsyncEnumerator(CancellationToken cancellationToken)
            => new TextSequence().GetAsyncEnumerator(cancellationToken);
    }
    public sealed class TextSequence : Base<string>
    { public TextSequence() : base("alpha", "beta", 0) { } }
    public struct Pair { public long X; public int Y; public Pair(long x, int y) { X = x; Y = y; } }
    public sealed class PairSequence : Base<Pair>
    { public PairSequence() : base(new Pair(9007199254740993L, 1), new Pair(9007199254740995L, 2), 0) { } }
    private sealed class FactorySource : IValueTaskSource<Sequence>
    {
        private ManualResetValueTaskSourceCore<Sequence> _core;
        public short Version => _core.Version;
        public void Complete() => _core.SetResult(new Sequence(10));
        public Sequence GetResult(short token)
        {
            _consumptions++;
            var result = _core.GetResult(token);
            _core.Reset();
            return result;
        }
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _core.OnCompleted(continuation, state, token, flags);
    }
}
