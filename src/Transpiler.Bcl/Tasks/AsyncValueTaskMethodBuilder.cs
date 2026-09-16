using System;
using System.Runtime.CompilerServices;
namespace Transpiler.Bcl.Tasks;

/// <summary>Uses the shared managed state-machine protocol; no pooling or allocation-elision promise.</summary>
public struct AsyncValueTaskMethodBuilder
{
    private AsyncTaskMethodBuilder _builder;
    public static AsyncValueTaskMethodBuilder Create() => new AsyncValueTaskMethodBuilder { _builder = AsyncTaskMethodBuilder.Create() };
    public ValueTask Task => new ValueTask(_builder.Task);
    public void SetResult() => _builder.SetResult();
    public void SetException(Exception exception) => _builder.SetException(exception);
    public void SetStateMachine(IAsyncStateMachine stateMachine) => _builder.SetStateMachine(stateMachine);
    public void Start<T>(ref T stateMachine) where T : IAsyncStateMachine => _builder.Start(ref stateMachine);
    public void AwaitOnCompleted<A,T>(ref A awaiter, ref T stateMachine) where A : INotifyCompletion where T : IAsyncStateMachine
        => _builder.AwaitOnCompleted(ref awaiter, ref stateMachine);
    public void AwaitUnsafeOnCompleted<A,T>(ref A awaiter, ref T stateMachine) where A : ICriticalNotifyCompletion where T : IAsyncStateMachine
        => _builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
}
public struct AsyncValueTaskMethodBuilder<R>
{
    private AsyncTaskMethodBuilder<R> _builder;
    public static AsyncValueTaskMethodBuilder<R> Create() => new AsyncValueTaskMethodBuilder<R> { _builder = AsyncTaskMethodBuilder<R>.Create() };
    public ValueTask<R> Task => new ValueTask<R>(_builder.Task);
    public void SetResult(R result) => _builder.SetResult(result);
    public void SetException(Exception exception) => _builder.SetException(exception);
    public void SetStateMachine(IAsyncStateMachine stateMachine) => _builder.SetStateMachine(stateMachine);
    public void Start<T>(ref T stateMachine) where T : IAsyncStateMachine => _builder.Start(ref stateMachine);
    public void AwaitOnCompleted<A,T>(ref A awaiter, ref T stateMachine) where A : INotifyCompletion where T : IAsyncStateMachine
        => _builder.AwaitOnCompleted(ref awaiter, ref stateMachine);
    public void AwaitUnsafeOnCompleted<A,T>(ref A awaiter, ref T stateMachine) where A : ICriticalNotifyCompletion where T : IAsyncStateMachine
        => _builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
}
