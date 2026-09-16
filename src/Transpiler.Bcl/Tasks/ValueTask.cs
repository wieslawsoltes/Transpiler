using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Transpiler.Bcl.Tasks;

/// <summary>Result/Task-backed ValueTask profile. IValueTaskSource constructors are deliberately not substituted.</summary>
public readonly struct ValueTask : IEquatable<ValueTask>
{
    private readonly Task? _task;
    public ValueTask(Task task) { _task = task ?? throw new ArgumentNullException(nameof(task)); }
    public static ValueTask CompletedTask => default;
    public bool IsCompleted => _task == null || _task.IsCompleted;
    public bool IsCompletedSuccessfully => _task == null || _task.IsCompletedSuccessfully;
    public bool IsFaulted => _task != null && _task.IsFaulted;
    public bool IsCanceled => _task != null && _task.IsCanceled;
    public Task AsTask() => _task ?? Task.CompletedTask;
    public ValueTask Preserve() => this;
    public ValueTaskAwaiter GetAwaiter() => new ValueTaskAwaiter(this);
    public ConfiguredValueTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) => new ConfiguredValueTaskAwaitable(this);
    internal void GetResult() { if (_task != null) _task.GetCompletion(); }
    internal void Register(Action continuation) => AsTask().Register(continuation);
    public bool Equals(ValueTask other) => object.ReferenceEquals(_task, other._task);
    public override bool Equals(object? other) => other is ValueTask value && Equals(value);
    public override int GetHashCode() => _task == null ? 0 : _task.GetHashCode();
    public static bool operator ==(ValueTask left, ValueTask right) => left.Equals(right);
    public static bool operator !=(ValueTask left, ValueTask right) => !left.Equals(right);
    public static ValueTask<T> FromResult<T>(T result) => new ValueTask<T>(result);
    public static ValueTask FromException(Exception exception) => new ValueTask(Task.FromException(exception));
    public static ValueTask<T> FromException<T>(Exception exception) => new ValueTask<T>(Task.FromException<T>(exception));
}

public readonly struct ValueTask<T> : IEquatable<ValueTask<T>>
{
    private readonly Task<T>? _task;
    private readonly T _result;
    public ValueTask(T result) { _task = null; _result = result; }
    public ValueTask(Task<T> task) { _task = task ?? throw new ArgumentNullException(nameof(task)); _result = default!; }
    public bool IsCompleted => _task == null || _task.IsCompleted;
    public bool IsCompletedSuccessfully => _task == null || _task.IsCompletedSuccessfully;
    public bool IsFaulted => _task != null && _task.IsFaulted;
    public bool IsCanceled => _task != null && _task.IsCanceled;
    public T Result => _task == null ? _result : _task.GetValue();
    public Task<T> AsTask() => _task ?? Task.FromResult(_result);
    public ValueTask<T> Preserve() => this;
    public ValueTaskAwaiter<T> GetAwaiter() => new ValueTaskAwaiter<T>(this);
    public ConfiguredValueTaskAwaitable<T> ConfigureAwait(bool continueOnCapturedContext) => new ConfiguredValueTaskAwaitable<T>(this);
    internal void Register(Action continuation) => AsTask().Register(continuation);
    public bool Equals(ValueTask<T> other) => _task != null || other._task != null
        ? object.ReferenceEquals(_task, other._task) : EqualityComparer<T>.Default.Equals(_result, other._result);
    public override bool Equals(object? other) => other is ValueTask<T> value && Equals(value);
    public override int GetHashCode() => _task == null ? (_result == null ? 0 : EqualityComparer<T>.Default.GetHashCode(_result)) : _task.GetHashCode();
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
    public void OnCompleted(Action continuation) => _value.Register(continuation);
    public void UnsafeOnCompleted(Action continuation) => _value.Register(continuation);
}
public readonly struct ValueTaskAwaiter<T> : ICriticalNotifyCompletion
{
    private readonly ValueTask<T> _value;
    internal ValueTaskAwaiter(ValueTask<T> value) { _value = value; }
    public bool IsCompleted => _value.IsCompleted;
    public T GetResult() => _value.Result;
    public void OnCompleted(Action continuation) => _value.Register(continuation);
    public void UnsafeOnCompleted(Action continuation) => _value.Register(continuation);
}
public readonly struct ConfiguredValueTaskAwaitable
{
    private readonly ValueTask _value;
    internal ConfiguredValueTaskAwaitable(ValueTask value) { _value = value; }
    public ConfiguredValueTaskAwaiter GetAwaiter() => new ConfiguredValueTaskAwaiter(_value);
    public readonly struct ConfiguredValueTaskAwaiter : ICriticalNotifyCompletion
    {
        private readonly ValueTask _value;
        internal ConfiguredValueTaskAwaiter(ValueTask value) { _value = value; }
        public bool IsCompleted => _value.IsCompleted;
        public void GetResult() => _value.GetResult();
        public void OnCompleted(Action continuation) => _value.Register(continuation);
        public void UnsafeOnCompleted(Action continuation) => _value.Register(continuation);
    }
}
public readonly struct ConfiguredValueTaskAwaitable<T>
{
    private readonly ValueTask<T> _value;
    internal ConfiguredValueTaskAwaitable(ValueTask<T> value) { _value = value; }
    public ConfiguredValueTaskAwaiter GetAwaiter() => new ConfiguredValueTaskAwaiter(_value);
    public readonly struct ConfiguredValueTaskAwaiter : ICriticalNotifyCompletion
    {
        private readonly ValueTask<T> _value;
        internal ConfiguredValueTaskAwaiter(ValueTask<T> value) { _value = value; }
        public bool IsCompleted => _value.IsCompleted;
        public T GetResult() => _value.Result;
        public void OnCompleted(Action continuation) => _value.Register(continuation);
        public void UnsafeOnCompleted(Action continuation) => _value.Register(continuation);
    }
}
