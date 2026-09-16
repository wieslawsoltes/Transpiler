using System;
using System.Runtime.CompilerServices;
namespace Transpiler.Bcl.Tasks;

/// <summary>Single-threaded FIFO continuation service. This is not a thread pool or a wall-clock timer.</summary>
public static class Scheduler
{
    private static readonly Queue<Action> Work = new Queue<Action>();
    public static void Post(Action continuation)
    { if (continuation == null) throw new ArgumentNullException(nameof(continuation)); Work.Enqueue(continuation); }
    public static bool RunOne()
    { if (!Work.TryDequeue(out Action next)) return false; next(); return true; }
}

/// <summary>Portable completion/continuation state. Every algorithm is translated from this managed IL.</summary>
public class Task
{
    private int _state; // 0 pending, 1 succeeded, 2 faulted, 3 canceled.
    private Exception? _error;
    private readonly List<Action> _continuations = new List<Action>();
    public bool IsCompleted => _state != 0;
    public bool IsCompletedSuccessfully => _state == 1;
    public bool IsFaulted => _state == 2;
    public bool IsCanceled => _state == 3;
    private static readonly Task Completed = CreateCompleted();
    private static Task CreateCompleted() { var task = new Task(); task.Finish(1, null); return task; }
    public static Task CompletedTask => Completed;
    public static Task<T> FromResult<T>(T value)
    { var task = new Task<T>(); task.Complete(value); return task; }
    public static Task FromException(Exception error)
    { var task = new Task(); task.Fail(error); return task; }
    public static Task<T> FromException<T>(Exception error)
    { var task = new Task<T>(); task.Fail(error); return task; }
    public static YieldAwaitable Yield() => new YieldAwaitable();
    public TaskAwaiter GetAwaiter() => new TaskAwaiter(this);
    public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) => new ConfiguredTaskAwaitable(this);
    internal void Register(Action continuation)
    {
        if (continuation == null) throw new ArgumentNullException(nameof(continuation));
        if (IsCompleted) Scheduler.Post(continuation); else _continuations.Add(continuation);
    }
    protected internal bool Finish(int state, Exception? error)
    {
        if (IsCompleted) return false;
        _error = error; _state = state;
        for (int i = 0; i < _continuations.Count; i++) Scheduler.Post(_continuations[i]);
        _continuations.Clear(); return true;
    }
    internal bool Fail(Exception error)
    {
        if (error == null) throw new ArgumentNullException(nameof(error));
        return Finish(2, error);
    }
    internal void SetAsyncException(Exception error)
    { if (error == null) throw new ArgumentNullException(nameof(error)); Finish(error is OperationCanceledException ? 3 : 2, error); }
    internal void GetCompletion()
    {
        int budget = 100000;
        while (!IsCompleted)
            if (--budget == 0 || !Scheduler.RunOne()) throw new InvalidOperationException("The cooperative task cannot make progress; no host completion is available.");
        if (_error != null) throw _error;
    }
}

public class Task<T> : Task
{
    private T _result = default!;
    internal bool Complete(T result)
    { if (IsCompleted) return false; _result = result; return Finish(1, null); }
    internal T GetValue() { GetCompletion(); return _result; }
    public new TaskAwaiter<T> GetAwaiter() => new TaskAwaiter<T>(this);
    public new ConfiguredTaskAwaitable<T> ConfigureAwait(bool continueOnCapturedContext) => new ConfiguredTaskAwaitable<T>(this);
}

public sealed class TaskCompletionSource<T>
{
    private readonly Task<T> _task = new Task<T>();
    public Task<T> Task => _task;
    public bool TrySetResult(T result) => _task.Complete(result);
    public void SetResult(T result)
    { if (!TrySetResult(result)) throw new InvalidOperationException("The task is already complete."); }
    public bool TrySetException(Exception error) => _task.Fail(error);
    public void SetException(Exception error)
    { if (!TrySetException(error)) throw new InvalidOperationException("The task is already complete."); }
    public bool TrySetCanceled() => _task.Finish(3, new System.Threading.Tasks.TaskCanceledException());
    public void SetCanceled()
    { if (!TrySetCanceled()) throw new InvalidOperationException("The task is already complete."); }
}

public readonly struct TaskAwaiter : ICriticalNotifyCompletion
{
    private readonly Task _task;
    internal TaskAwaiter(Task task) { _task = task; }
    public bool IsCompleted => _task.IsCompleted;
    public void GetResult() => _task.GetCompletion();
    public void OnCompleted(Action continuation) => _task.Register(continuation);
    public void UnsafeOnCompleted(Action continuation) => _task.Register(continuation);
}
public readonly struct TaskAwaiter<T> : ICriticalNotifyCompletion
{
    private readonly Task<T> _task;
    internal TaskAwaiter(Task<T> task) { _task = task; }
    public bool IsCompleted => _task.IsCompleted;
    public T GetResult() => _task.GetValue();
    public void OnCompleted(Action continuation) => _task.Register(continuation);
    public void UnsafeOnCompleted(Action continuation) => _task.Register(continuation);
}
public readonly struct YieldAwaitable
{
    public YieldAwaiter GetAwaiter() => new YieldAwaiter();
    public readonly struct YieldAwaiter : ICriticalNotifyCompletion
    {
        public bool IsCompleted => false;
        public void GetResult() { }
        public void OnCompleted(Action continuation) => Scheduler.Post(continuation);
        public void UnsafeOnCompleted(Action continuation) => Scheduler.Post(continuation);
    }
}
public readonly struct ConfiguredTaskAwaitable
{
    private readonly Task _task;
    internal ConfiguredTaskAwaitable(Task task) { _task = task; }
    public ConfiguredTaskAwaiter GetAwaiter() => new ConfiguredTaskAwaiter(_task);
    public readonly struct ConfiguredTaskAwaiter : ICriticalNotifyCompletion
    {
        private readonly Task _task;
        internal ConfiguredTaskAwaiter(Task task) { _task = task; }
        public bool IsCompleted => _task.IsCompleted;
        public void GetResult() => _task.GetCompletion();
        public void OnCompleted(Action continuation) => _task.Register(continuation);
        public void UnsafeOnCompleted(Action continuation) => _task.Register(continuation);
    }
}
public readonly struct ConfiguredTaskAwaitable<T>
{
    private readonly Task<T> _task;
    internal ConfiguredTaskAwaitable(Task<T> task) { _task = task; }
    public ConfiguredTaskAwaiter GetAwaiter() => new ConfiguredTaskAwaiter(_task);
    public readonly struct ConfiguredTaskAwaiter : ICriticalNotifyCompletion
    {
        private readonly Task<T> _task;
        internal ConfiguredTaskAwaiter(Task<T> task) { _task = task; }
        public bool IsCompleted => _task.IsCompleted;
        public T GetResult() => _task.GetValue();
        public void OnCompleted(Action continuation) => _task.Register(continuation);
        public void UnsafeOnCompleted(Action continuation) => _task.Register(continuation);
    }
}
