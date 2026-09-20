using System;
using Transpiler.Bcl.Async;
using Transpiler.Bcl.Tasks;

namespace Transpiler.Bcl.Interop;

/// <summary>A pending factory result owned by one stream cursor, not a host-language promise.</summary>
public interface IStreamFactory<T>
{
    bool IsCompleted { get; }
    IAsyncEnumerable<T> Finish();
}

/// <summary>
/// Bridges declared concrete/interface/erased results to an explicitly selected enumerable contract.
/// The ValueTask path calls AsTask exactly once. No source operation is reissued by cleanup retries.
/// Algorithms and runtime casts are translated managed IL, including boxing value-type enumerables.
/// </summary>
public sealed class StreamFactory<TSource, TElement> : IStreamFactory<TElement>
{
    private Task<TSource>? _task;
    private StreamFactory(Task<TSource> task) { _task = task; }

    public static StreamCursor<TElement> Open(TSource value)
        => StreamCursor<TElement>.Open((IAsyncEnumerable<TElement>)(object)value!);

    public static StreamCursor<TElement> OpenTask(Task<TSource> task)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));
        return StreamCursor<TElement>.FromFactory(new StreamFactory<TSource, TElement>(task));
    }

    public static StreamCursor<TElement> OpenValueTask(ValueTask<TSource> value) => OpenTask(value.AsTask());

    public bool IsCompleted => _task != null && _task.IsCompleted;

    public IAsyncEnumerable<TElement> Finish()
    {
        var task = _task;
        if (task == null || !task.IsCompleted) throw new InvalidOperationException("No completed stream factory is available.");
        _task = null; // Retire before user source/result code or a checked cast can throw.
        return (IAsyncEnumerable<TElement>)(object)task.GetAwaiter().GetResult()!;
    }
}
