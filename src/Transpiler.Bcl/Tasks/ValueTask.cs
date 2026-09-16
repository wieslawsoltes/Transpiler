using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Transpiler.Bcl.Tasks.Sources;

namespace Transpiler.Bcl.Tasks;

/// <summary>Result/Task/source-backed value with exact source token and continuation-flag forwarding.</summary>
public readonly struct ValueTask : IEquatable<ValueTask>
{
    private readonly Task? _task;
    private readonly IValueTaskSource? _source;
    private readonly short _token;
    public ValueTask(Task task)
    { _task = task ?? throw new ArgumentNullException(nameof(task)); _source = null; _token = 0; }
    public ValueTask(IValueTaskSource source, short token)
    { _source = source ?? throw new ArgumentNullException(nameof(source)); _task = null; _token = token; }
    public static ValueTask CompletedTask => default;
    public bool IsCompleted => _source != null ? _source.GetStatus(_token) != ValueTaskSourceStatus.Pending : _task == null || _task.IsCompleted;
    public bool IsCompletedSuccessfully => _source != null ? _source.GetStatus(_token) == ValueTaskSourceStatus.Succeeded : _task == null || _task.IsCompletedSuccessfully;
    public bool IsFaulted => _source != null ? _source.GetStatus(_token) == ValueTaskSourceStatus.Faulted : _task != null && _task.IsFaulted;
    public bool IsCanceled => _source != null ? _source.GetStatus(_token) == ValueTaskSourceStatus.Canceled : _task != null && _task.IsCanceled;
    internal void GetResult() { if (_source != null) _source.GetResult(_token); else if (_task != null) _task.GetCompletion(); }
    public Task AsTask() => _source != null ? ValueTaskSourceBridge.Create(_source, _token) : _task ?? Task.CompletedTask;
    public ValueTask Preserve() => _source == null ? this : new ValueTask(AsTask());
    public ValueTaskAwaiter GetAwaiter() => new ValueTaskAwaiter(this);
    public ConfiguredValueTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) => new ConfiguredValueTaskAwaitable(this, continueOnCapturedContext);
    internal void Register(Action continuation, bool captureContext, bool flowExecutionContext)
    {
        if (continuation == null) throw new ArgumentNullException(nameof(continuation));
        if (_source != null)
        {
            var flags = (captureContext ? ValueTaskSourceOnCompletedFlags.UseSchedulingContext : ValueTaskSourceOnCompletedFlags.None) |
                (flowExecutionContext ? ValueTaskSourceOnCompletedFlags.FlowExecutionContext : ValueTaskSourceOnCompletedFlags.None);
            _source.OnCompleted(RunContinuation, continuation, _token, flags);
        }
        else AsTask().Register(continuation);
    }
    private static void RunContinuation(object? state) => ((Action)state!)();
    public bool Equals(ValueTask other)
    {
        if (_source != null || other._source != null) return object.ReferenceEquals(_source, other._source) && _token == other._token;
        return object.ReferenceEquals(_task, other._task);
    }
    public override bool Equals(object? other) => other is ValueTask value && Equals(value);
    public override int GetHashCode() => _source != null ? _source.GetHashCode() : _task != null ? _task.GetHashCode() : 0;
    public static bool operator ==(ValueTask left, ValueTask right) => left.Equals(right);
    public static bool operator !=(ValueTask left, ValueTask right) => !left.Equals(right);
    public static ValueTask FromCanceled(Threading.CancellationToken token) => new ValueTask(Task.FromCanceled(token));
    public static ValueTask<T> FromCanceled<T>(Threading.CancellationToken token) => new ValueTask<T>(Task.FromCanceled<T>(token));
    public static ValueTask<T> FromResult<T>(T result) => new ValueTask<T>(result);
    public static ValueTask FromException(Exception error) => new ValueTask(Task.FromException(error));
    public static ValueTask<T> FromException<T>(Exception error) => new ValueTask<T>(Task.FromException<T>(error));
}

/// <summary>Result/Task/source-backed value with exact source token and continuation-flag forwarding.</summary>
public readonly struct ValueTask<T> : IEquatable<ValueTask<T>>
{
    private readonly Task<T>? _task;
    private readonly IValueTaskSource<T>? _source;
    private readonly short _token;
    private readonly T _result;
    public ValueTask(T result) { _task = null; _source = null; _token = 0; _result = result; }
    public ValueTask(Task<T> task)
    { _task = task ?? throw new ArgumentNullException(nameof(task)); _source = null; _token = 0; _result = default!; }
    public ValueTask(IValueTaskSource<T> source, short token)
    { _source = source ?? throw new ArgumentNullException(nameof(source)); _task = null; _token = token; _result = default!; }
    public bool IsCompleted => _source != null ? _source.GetStatus(_token) != ValueTaskSourceStatus.Pending : _task == null || _task.IsCompleted;
    public bool IsCompletedSuccessfully => _source != null ? _source.GetStatus(_token) == ValueTaskSourceStatus.Succeeded : _task == null || _task.IsCompletedSuccessfully;
    public bool IsFaulted => _source != null ? _source.GetStatus(_token) == ValueTaskSourceStatus.Faulted : _task != null && _task.IsFaulted;
    public bool IsCanceled => _source != null ? _source.GetStatus(_token) == ValueTaskSourceStatus.Canceled : _task != null && _task.IsCanceled;
    public T Result => _source != null ? _source.GetResult(_token) : _task == null ? _result : _task.GetValue();
    public Task<T> AsTask() => _source != null ? ValueTaskSourceBridge<T>.Create(_source, _token) : _task ?? Task.FromResult(_result);
    public ValueTask<T> Preserve() => _source == null ? this : new ValueTask<T>(AsTask());
    public ValueTaskAwaiter<T> GetAwaiter() => new ValueTaskAwaiter<T>(this);
    public ConfiguredValueTaskAwaitable<T> ConfigureAwait(bool continueOnCapturedContext) => new ConfiguredValueTaskAwaitable<T>(this, continueOnCapturedContext);
    internal void Register(Action continuation, bool captureContext, bool flowExecutionContext)
    {
        if (continuation == null) throw new ArgumentNullException(nameof(continuation));
        if (_source != null)
        {
            var flags = (captureContext ? ValueTaskSourceOnCompletedFlags.UseSchedulingContext : ValueTaskSourceOnCompletedFlags.None) |
                (flowExecutionContext ? ValueTaskSourceOnCompletedFlags.FlowExecutionContext : ValueTaskSourceOnCompletedFlags.None);
            _source.OnCompleted(RunContinuation, continuation, _token, flags);
        }
        else AsTask().Register(continuation);
    }
    private static void RunContinuation(object? state) => ((Action)state!)();
    public bool Equals(ValueTask<T> other)
    {
        if (_source != null || other._source != null) return object.ReferenceEquals(_source, other._source) && _token == other._token;
        return _task != null || other._task != null ? object.ReferenceEquals(_task, other._task) : EqualityComparer<T>.Default.Equals(_result, other._result);
    }
    public override bool Equals(object? other) => other is ValueTask<T> value && Equals(value);
    public override int GetHashCode() => _source != null ? _source.GetHashCode() : _task != null ? _task.GetHashCode() : (_result == null ? 0 : EqualityComparer<T>.Default.GetHashCode(_result));
    public static bool operator ==(ValueTask<T> left, ValueTask<T> right) => left.Equals(right);
    public static bool operator !=(ValueTask<T> left, ValueTask<T> right) => !left.Equals(right);
    public override string ToString() => IsCompletedSuccessfully ? (Result == null ? "" : Result.ToString()!) : "";
}

public readonly struct ValueTaskAwaiter : ICriticalNotifyCompletion
{
    private readonly ValueTask _value;
    internal ValueTaskAwaiter(ValueTask value) { _value = value; }
    public bool IsCompleted => _value.IsCompleted;
    public void GetResult() => _value.GetResult();
    public void OnCompleted(Action continuation) => _value.Register(continuation, true, true);
    public void UnsafeOnCompleted(Action continuation) => _value.Register(continuation, true, false);
}
public readonly struct ConfiguredValueTaskAwaitable
{
    private readonly ValueTask _value;
    private readonly bool _captureContext;
    internal ConfiguredValueTaskAwaitable(ValueTask value, bool captureContext) { _value = value; _captureContext = captureContext; }
    public ConfiguredValueTaskAwaiter GetAwaiter() => new ConfiguredValueTaskAwaiter(_value, _captureContext);
    public readonly struct ConfiguredValueTaskAwaiter : ICriticalNotifyCompletion
    {
        private readonly ValueTask _value;
        private readonly bool _captureContext;
        internal ConfiguredValueTaskAwaiter(ValueTask value, bool captureContext) { _value = value; _captureContext = captureContext; }
        public bool IsCompleted => _value.IsCompleted;
        public void GetResult() => _value.GetResult();
        public void OnCompleted(Action continuation) => _value.Register(continuation, _captureContext, true);
        public void UnsafeOnCompleted(Action continuation) => _value.Register(continuation, _captureContext, false);
    }
}

public readonly struct ValueTaskAwaiter<T> : ICriticalNotifyCompletion
{
    private readonly ValueTask<T> _value;
    internal ValueTaskAwaiter(ValueTask<T> value) { _value = value; }
    public bool IsCompleted => _value.IsCompleted;
    public T GetResult() => _value.Result;
    public void OnCompleted(Action continuation) => _value.Register(continuation, true, true);
    public void UnsafeOnCompleted(Action continuation) => _value.Register(continuation, true, false);
}
public readonly struct ConfiguredValueTaskAwaitable<T>
{
    private readonly ValueTask<T> _value;
    private readonly bool _captureContext;
    internal ConfiguredValueTaskAwaitable(ValueTask<T> value, bool captureContext) { _value = value; _captureContext = captureContext; }
    public ConfiguredValueTaskAwaiter GetAwaiter() => new ConfiguredValueTaskAwaiter(_value, _captureContext);
    public readonly struct ConfiguredValueTaskAwaiter : ICriticalNotifyCompletion
    {
        private readonly ValueTask<T> _value;
        private readonly bool _captureContext;
        internal ConfiguredValueTaskAwaiter(ValueTask<T> value, bool captureContext) { _value = value; _captureContext = captureContext; }
        public bool IsCompleted => _value.IsCompleted;
        public T GetResult() => _value.Result;
        public void OnCompleted(Action continuation) => _value.Register(continuation, _captureContext, true);
        public void UnsafeOnCompleted(Action continuation) => _value.Register(continuation, _captureContext, false);
    }
}

