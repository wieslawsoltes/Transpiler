using System.Runtime.CompilerServices;
namespace Transpiler.Bcl.Tasks;

/// <summary>Uses the shared managed continuation protocol for Roslyn's actual async-iterator state machine.</summary>
public struct AsyncIteratorMethodBuilder
{
    private AsyncTaskMethodBuilder _builder;
    public static AsyncIteratorMethodBuilder Create() => new AsyncIteratorMethodBuilder { _builder = AsyncTaskMethodBuilder.Create() };
    public void MoveNext<T>(ref T stateMachine) where T : IAsyncStateMachine => _builder.Start(ref stateMachine);
    public void AwaitOnCompleted<A,T>(ref A awaiter, ref T stateMachine) where A : INotifyCompletion where T : IAsyncStateMachine
        => _builder.AwaitOnCompleted(ref awaiter, ref stateMachine);
    public void AwaitUnsafeOnCompleted<A,T>(ref A awaiter, ref T stateMachine) where A : ICriticalNotifyCompletion where T : IAsyncStateMachine
        => _builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    public void Complete() => _builder.SetResult();
}
