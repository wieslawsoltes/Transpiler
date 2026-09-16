using System;

namespace Transpiler.Bcl.Tasks.Sources;

public enum ValueTaskSourceStatus { Pending = 0, Succeeded = 1, Faulted = 2, Canceled = 3 }
[Flags]
public enum ValueTaskSourceOnCompletedFlags { None = 0, UseSchedulingContext = 1, FlowExecutionContext = 2 }

public interface IValueTaskSource
{
    ValueTaskSourceStatus GetStatus(short token);
    void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags);
    void GetResult(short token);
}

public interface IValueTaskSource<out T>
{
    ValueTaskSourceStatus GetStatus(short token);
    void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags);
    T GetResult(short token);
}

/// <summary>
/// Single-threaded, reusable completion core. Reset invalidates old tokens; a generation has one consumer.
/// Flags are accepted in the declared no-context cooperative profile, not a claim of context/thread-pool support.
/// </summary>
public struct ManualResetValueTaskSourceCore<T>
{
    private T _result;
    private Exception? _error;
    private Action<object?>? _continuation;
    private object? _state;
    private short _version;
    private bool _completed;
    private bool _registered;
    public bool RunContinuationsAsynchronously { get; set; }
    public short Version => _version;

    public void Reset()
    {
        _version = unchecked((short)(_version + 1));
        _result = default!; _error = null; _continuation = null; _state = null;
        _completed = false; _registered = false;
    }
    private void Validate(short token)
    { if (token != _version) throw new InvalidOperationException("The completion token is stale."); }
    public ValueTaskSourceStatus GetStatus(short token)
    {
        Validate(token);
        return !_completed ? ValueTaskSourceStatus.Pending : _error == null ? ValueTaskSourceStatus.Succeeded :
            _error is OperationCanceledException ? ValueTaskSourceStatus.Canceled : ValueTaskSourceStatus.Faulted;
    }
    public T GetResult(short token)
    {
        Validate(token);
        if (!_completed) throw new InvalidOperationException("The source has not completed.");
        if (_error != null) throw _error;
        return _result;
    }
    public void SetResult(T result)
    {
        if (_completed) throw new InvalidOperationException("The source is already complete.");
        _result = result; Complete();
    }
    public void SetException(Exception error)
    {
        if (error == null) throw new ArgumentNullException(nameof(error));
        if (_completed) throw new InvalidOperationException("The source is already complete.");
        _error = error; Complete();
    }
    private void Complete()
    {
        _completed = true;
        var continuation = _continuation; var state = _state;
        // Clear before invoking: the consumer may synchronously GetResult, Reset and register the next operation.
        _continuation = null; _state = null;
        if (continuation == null) return;
        if (RunContinuationsAsynchronously) Scheduler.Post(() => continuation(state));
        else continuation(state);
    }
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        if (continuation == null) throw new ArgumentNullException(nameof(continuation));
        Validate(token);
        if (_registered) throw new InvalidOperationException("Only one continuation is allowed per operation.");
        _registered = true;
        // Register-after-completion always queues, avoiding a synchronous continuation inside registration.
        if (_completed) Scheduler.Post(() => continuation(state));
        else { _continuation = continuation; _state = state; }
    }
}
