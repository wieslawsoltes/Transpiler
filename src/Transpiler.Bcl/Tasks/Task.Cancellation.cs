using System;
using Transpiler.Bcl.Threading;
namespace Transpiler.Bcl.Tasks;

public partial class Task
{
    internal CancellationToken Cancellation;
    internal bool Cancel(CancellationToken token)
    {
        if (IsCompleted) return false;
        Cancellation = token;
        return Finish(3, new TaskCanceledException(this));
    }
    public static Task FromCanceled(CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested) throw new ArgumentOutOfRangeException(nameof(cancellationToken));
        var result = new Task(); result.Cancel(cancellationToken); return result;
    }
    public static Task<T> FromCanceled<T>(CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested) throw new ArgumentOutOfRangeException(nameof(cancellationToken));
        var result = new Task<T>(); result.Cancel(cancellationToken); return result;
    }
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        if (IsCompleted || !cancellationToken.CanBeCanceled) return this;
        if (cancellationToken.IsCancellationRequested) return FromCanceled(cancellationToken);
        var promise = new WaitPromise(this, new Task(), cancellationToken); promise.Start(); return promise.Result;
    }
    internal class WaitPromise
    {
        protected readonly Task Input;
        internal readonly Task Result;
        private readonly CancellationToken _token;
        private CompletionRegistration? _completion;
        private CancellationTokenRegistration _cancellation;
        internal WaitPromise(Task input, Task result, CancellationToken token) { Input = input; Result = result; _token = token; }
        internal void Start() { _completion = Input.Observe(Completed); _cancellation = _token.Register(Canceled); }
        private void Cleanup() { _completion?.Detach(); _completion = null; _cancellation.Dispose(); }
        private void Canceled() { if (Result.Cancel(_token)) Cleanup(); }
        private void Completed()
        {
            if (Result.IsCompleted) return;
            if (Input.IsFaulted) Result.Finish(2, Input.CompletionError, Input.Errors);
            else if (Input.IsCanceled) Result.Cancel(Input.Cancellation);
            else Succeed();
            Cleanup();
        }
        protected virtual void Succeed() => Result.Finish(1, null);
    }
}
public partial class Task<T>
{
    public new Task<T> WaitAsync(CancellationToken cancellationToken)
    {
        if (IsCompleted || !cancellationToken.CanBeCanceled) return this;
        if (cancellationToken.IsCancellationRequested) return FromCanceled<T>(cancellationToken);
        var result = new Task<T>(); var promise = new TypedWaitPromise(this, result, cancellationToken); promise.Start(); return result;
    }
    private sealed class TypedWaitPromise : WaitPromise
    {
        internal TypedWaitPromise(Task<T> input, Task<T> result, CancellationToken token) : base(input, result, token) { }
        protected override void Succeed() => ((Task<T>)Result).Complete(((Task<T>)Input).GetValue());
    }
}
public sealed partial class TaskCompletionSource<T>
{
    public bool TrySetCanceled(CancellationToken cancellationToken) => _task.Cancel(cancellationToken);
    public void SetCanceled(CancellationToken cancellationToken)
    { if (!TrySetCanceled(cancellationToken)) throw new InvalidOperationException("The task is already complete."); }
}
