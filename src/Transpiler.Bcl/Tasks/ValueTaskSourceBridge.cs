using System;
using Transpiler.Bcl.Tasks.Sources;
namespace Transpiler.Bcl.Tasks;

/// <summary>Consumes one source operation; detaches its source before publishing task completion.</summary>
internal sealed class ValueTaskSourceBridge
{
    private IValueTaskSource? _source;
    private readonly short _token;
    private readonly Task _task = new Task();
    private ValueTaskSourceBridge(IValueTaskSource source, short token) { _source = source; _token = token; }
    internal static Task Create(IValueTaskSource source, short token)
    {
        var status = source.GetStatus(token);
        var bridge = new ValueTaskSourceBridge(source, token);
        if (status == ValueTaskSourceStatus.Pending) source.OnCompleted(Completed, bridge, token, ValueTaskSourceOnCompletedFlags.None);
        else bridge.Complete();
        return bridge._task;
    }
    private static void Completed(object? state) => ((ValueTaskSourceBridge)state!).Complete();
    private void Complete()
    {
        var source = _source;
        if (source == null) return;
        _source = null;
        var status = source.GetStatus(_token);
        try
        {
            source.GetResult(_token); _task.Finish(1, null);
        }
        catch (Exception error)
        {
            if (status == ValueTaskSourceStatus.Canceled)
                _task.Cancel(error is OperationCanceledException canceled ? canceled.CancellationToken : new Threading.CancellationToken(true));
            else _task.Fail(error);
        }
    }
}

/// <summary>Consumes one source operation; detaches its source before publishing task completion.</summary>
internal sealed class ValueTaskSourceBridge<T>
{
    private IValueTaskSource<T>? _source;
    private readonly short _token;
    private readonly Task<T> _task = new Task<T>();
    private ValueTaskSourceBridge(IValueTaskSource<T> source, short token) { _source = source; _token = token; }
    internal static Task<T> Create(IValueTaskSource<T> source, short token)
    {
        var status = source.GetStatus(token);
        var bridge = new ValueTaskSourceBridge<T>(source, token);
        if (status == ValueTaskSourceStatus.Pending) source.OnCompleted(Completed, bridge, token, ValueTaskSourceOnCompletedFlags.None);
        else bridge.Complete();
        return bridge._task;
    }
    private static void Completed(object? state) => ((ValueTaskSourceBridge<T>)state!).Complete();
    private void Complete()
    {
        var source = _source;
        if (source == null) return;
        _source = null;
        var status = source.GetStatus(_token);
        try
        {
            var result = source.GetResult(_token); _task.Complete(result);
        }
        catch (Exception error)
        {
            if (status == ValueTaskSourceStatus.Canceled)
                _task.Cancel(error is OperationCanceledException canceled ? canceled.CancellationToken : new Threading.CancellationToken(true));
            else _task.Fail(error);
        }
    }
}

