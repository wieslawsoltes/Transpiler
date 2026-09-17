using System;
using Transpiler.Bcl.Threading;

namespace Transpiler.Bcl.Tasks;

public partial class Task
{
    public Task WaitAsync(TimeSpan timeout) => WaitAsync(timeout, default(CancellationToken));
    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => TimedWait(Timeout.Validate(timeout, nameof(timeout)), TimeProvider.System, cancellationToken);
    public Task WaitAsync(TimeSpan timeout, TimeProvider timeProvider) => WaitAsync(timeout, timeProvider, default);
    public Task WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (timeProvider == null) throw new ArgumentNullException(nameof(timeProvider));
        return TimedWait(Timeout.Validate(timeout, nameof(timeout)), timeProvider, cancellationToken);
    }
    private Task TimedWait(long milliseconds, TimeProvider provider, CancellationToken token)
    {
        if (IsCompleted || (milliseconds == -1 && !token.CanBeCanceled)) return this;
        if (token.IsCancellationRequested) return FromCanceled(token);
        if (milliseconds == 0) return FromException(new TimeoutException());
        var promise = new TimeoutPromise(this, new Task(), milliseconds, provider, token);
        promise.Start(); return promise.Result;
    }
    internal class TimeoutPromise
    {
        private Task? _input;
        internal readonly Task Result;
        private readonly long _milliseconds;
        private TimeProvider? _provider;
        private CancellationToken _token;
        private CompletionRegistration? _completion;
        private CancellationTokenRegistration _registration;
        private ITimer? _timer;
        internal TimeoutPromise(Task input, Task result, long milliseconds, TimeProvider provider, CancellationToken token)
        { _input = input; Result = result; _milliseconds = milliseconds; _provider = provider; _token = token; }
        internal void Start()
        {
            try
            {
                _completion = _input!.Observe(Completed);
                if (Result.IsCompleted) { Cleanup(); return; }
                if (_milliseconds != -1)
                {
                    var timer = _provider!.CreateTimer(_ => Expired(), null, TimeSpan.FromMilliseconds(_milliseconds), Timeout.InfiniteTimeSpan);
                    _timer = timer;
                    if (Result.IsCompleted) { timer.Dispose(); _timer = null; return; }
                }
                _provider = null;
                var registration = _token.Register(Canceled);
                _registration = registration;
                if (Result.IsCompleted) { registration.Dispose(); _registration = default; }
            }
            catch { Cleanup(); throw; }
        }
        private void Cleanup()
        {
            _completion?.Detach(); _completion = null;
            _registration.Dispose(); _registration = default;
            var timer = _timer; _timer = null;
            _input = null; _provider = null; _token = default;
            timer?.Dispose();
        }
        private void Expired()
        { if (!Result.IsCompleted) { try { Result.Fail(new TimeoutException()); } finally { Cleanup(); } } }
        private void Canceled()
        { if (!Result.IsCompleted) { var token = _token; try { Result.Cancel(token); } finally { Cleanup(); } } }
        private void Completed()
        {
            if (Result.IsCompleted) return;
            var input = _input!;
            try
            {
                if (input.IsFaulted) Result.Finish(2, input.CompletionError, input.Errors);
                else if (input.IsCanceled) Result.Finish(3, input.CompletionError);
                else Succeed(input);
            }
            finally { Cleanup(); }
        }
        protected virtual void Succeed(Task input) => Result.Finish(1, null);
    }
}

public partial class Task<T>
{
    public new Task<T> WaitAsync(TimeSpan timeout) => WaitAsync(timeout, default(CancellationToken));
    public new Task<T> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => TimedWait(Timeout.Validate(timeout, nameof(timeout)), TimeProvider.System, cancellationToken);
    public new Task<T> WaitAsync(TimeSpan timeout, TimeProvider timeProvider) => WaitAsync(timeout, timeProvider, default);
    public new Task<T> WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (timeProvider == null) throw new ArgumentNullException(nameof(timeProvider));
        return TimedWait(Timeout.Validate(timeout, nameof(timeout)), timeProvider, cancellationToken);
    }
    private Task<T> TimedWait(long milliseconds, TimeProvider provider, CancellationToken token)
    {
        if (IsCompleted || (milliseconds == -1 && !token.CanBeCanceled)) return this;
        if (token.IsCancellationRequested) return FromCanceled<T>(token);
        if (milliseconds == 0) return FromException<T>(new TimeoutException());
        var result = new Task<T>();
        var promise = new TypedTimeoutPromise(this, result, milliseconds, provider, token);
        promise.Start(); return result;
    }
    private sealed class TypedTimeoutPromise : TimeoutPromise
    {
        internal TypedTimeoutPromise(Task<T> input, Task<T> result, long milliseconds, TimeProvider provider, CancellationToken token)
            : base(input, result, milliseconds, provider, token) { }
        protected override void Succeed(Task input) => ((Task<T>)Result).Complete(((Task<T>)input).GetValue());
    }
}
