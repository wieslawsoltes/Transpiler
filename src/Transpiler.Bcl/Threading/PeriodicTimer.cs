using System;
using Transpiler.Bcl.Tasks;
using Transpiler.Bcl.Tasks.Sources;

namespace Transpiler.Bcl.Threading;

/// <summary>One consumer, coalesced ticks, explicit disposal, and cancellation scoped to one wait.</summary>
public sealed class PeriodicTimer : IDisposable
{
    private readonly State _state = new();
    private readonly ITimer _timer;
    private TimeSpan _period;
    public PeriodicTimer(TimeSpan period) : this(period, TimeProvider.System) { }
    public PeriodicTimer(TimeSpan period, TimeProvider timeProvider)
    {
        if (timeProvider == null) throw new ArgumentNullException(nameof(timeProvider));
        ValidatePeriod(period, nameof(period));
        _period = period;
        var state = _state;
        _timer = timeProvider.CreateTimer(_ => state.Signal(false, default), null, period, period);
    }
    private static void ValidatePeriod(TimeSpan value, string name)
    {
        long milliseconds = checked((long)value.TotalMilliseconds);
        if (value != Timeout.InfiniteTimeSpan && (milliseconds < 1 || milliseconds > Timeout.MaximumMilliseconds))
            throw new ArgumentOutOfRangeException(name);
    }
    public TimeSpan Period
    {
        get => _period;
        set
        {
            ValidatePeriod(value, nameof(value));
            if (!_timer.Change(value, value)) throw new ObjectDisposedException("PeriodicTimer");
            _period = value;
        }
    }
    public ValueTask<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default) => _state.Wait(cancellationToken);
    public void Dispose()
    {
        _timer.Dispose();
        _state.Signal(true, default);
    }
    private sealed class State : IValueTaskSource<bool>
    {
        private ManualResetValueTaskSourceCore<bool> _core;
        private CancellationTokenRegistration _registration;
        private bool _stopped;
        private bool _signaled;
        private bool _active;
        internal ValueTask<bool> Wait(CancellationToken token)
        {
            if (_active) throw new InvalidOperationException("Only one tick wait may be outstanding.");
            if (token.IsCancellationRequested) return ValueTask.FromCanceled<bool>(token);
            if (_signaled)
            {
                if (!_stopped) _signaled = false;
                return new ValueTask<bool>(!_stopped);
            }
            _active = true;
            _registration = token.Register(() => Signal(false, token));
            return new ValueTask<bool>(this, _core.Version);
        }
        internal void Signal(bool stopping, CancellationToken token)
        {
            _stopped |= stopping;
            if (_signaled) return;
            _signaled = true;
            if (!_active) return;
            if (token.IsCancellationRequested) _core.SetException(new OperationCanceledException(token));
            else _core.SetResult(true);
        }
        public bool GetResult(short token)
        {
            // Reject stale/early consumption without resetting a different live operation.
            if (_core.GetStatus(token) == ValueTaskSourceStatus.Pending) throw new InvalidOperationException("The tick is pending.");
            _registration.Dispose(); _registration = default;
            try { _core.GetResult(token); return !_stopped; }
            finally { _core.Reset(); _active = false; if (!_stopped) _signaled = false; }
        }
        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _core.OnCompleted(continuation, state, token, flags);
    }
}
