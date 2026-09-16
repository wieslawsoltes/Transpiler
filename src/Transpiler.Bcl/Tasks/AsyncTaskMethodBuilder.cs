using System;
using System.Runtime.CompilerServices;
namespace Transpiler.Bcl.Tasks;

/// <summary>A suspended state-machine value is copied into owned storage; its builder shares the task object.</summary>
internal sealed class StateMachineBox<T> where T : IAsyncStateMachine
{
    public T StateMachine = default!;
    public void MoveNext() => StateMachine.MoveNext();
}
public struct AsyncTaskMethodBuilder
{
    private Task _task;
    public static AsyncTaskMethodBuilder Create() => new AsyncTaskMethodBuilder { _task = new Task() };
    public Task Task => _task;
    public void SetResult() => _task.Finish(1, null);
    public void SetException(Exception error) => _task.Fail(error);
    public void SetStateMachine(IAsyncStateMachine stateMachine)
    { if (stateMachine == null) throw new ArgumentNullException(nameof(stateMachine)); }
    public void Start<T>(ref T stateMachine) where T : IAsyncStateMachine => stateMachine.MoveNext();
    public void AwaitOnCompleted<A,T>(ref A awaiter, ref T stateMachine) where A : INotifyCompletion where T : IAsyncStateMachine
    { var box = new StateMachineBox<T>(); box.StateMachine = stateMachine; awaiter.OnCompleted(box.MoveNext); }
    public void AwaitUnsafeOnCompleted<A,T>(ref A awaiter, ref T stateMachine) where A : ICriticalNotifyCompletion where T : IAsyncStateMachine
    { var box = new StateMachineBox<T>(); box.StateMachine = stateMachine; awaiter.UnsafeOnCompleted(box.MoveNext); }
}
public struct AsyncTaskMethodBuilder<R>
{
    private Task<R> _task;
    public static AsyncTaskMethodBuilder<R> Create() => new AsyncTaskMethodBuilder<R> { _task = new Task<R>() };
    public Task<R> Task => _task;
    public void SetResult(R result) => _task.Complete(result);
    public void SetException(Exception error) => _task.Fail(error);
    public void SetStateMachine(IAsyncStateMachine stateMachine)
    { if (stateMachine == null) throw new ArgumentNullException(nameof(stateMachine)); }
    public void Start<T>(ref T stateMachine) where T : IAsyncStateMachine => stateMachine.MoveNext();
    public void AwaitOnCompleted<A,T>(ref A awaiter, ref T stateMachine) where A : INotifyCompletion where T : IAsyncStateMachine
    { var box = new StateMachineBox<T>(); box.StateMachine = stateMachine; awaiter.OnCompleted(box.MoveNext); }
    public void AwaitUnsafeOnCompleted<A,T>(ref A awaiter, ref T stateMachine) where A : ICriticalNotifyCompletion where T : IAsyncStateMachine
    { var box = new StateMachineBox<T>(); box.StateMachine = stateMachine; awaiter.UnsafeOnCompleted(box.MoveNext); }
}
