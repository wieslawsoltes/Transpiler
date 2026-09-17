using System;
using Transpiler.Bcl.Async;
using Transpiler.Bcl.Tasks;

namespace Transpiler.Bcl.Threading;

public delegate void TimerCallback(object? state);
public interface ITimer : IDisposable, Async.IAsyncDisposable
{
    bool Change(TimeSpan dueTime, TimeSpan period);
}

/// <summary>Serialized cooperative timer. Native notifications never invoke managed user code.</summary>
public sealed class Timer : ITimer
{
    private HostTimer? _timer;
    private TimerCallback? _callback;
    private object? _state;
    private long _period;
    private bool _disposed;
    private int _running;
    private TaskCompletionSource<bool>? _drained;
    public Timer(TimerCallback callback)
    { Initialize(callback, this, -1, -1); }
    public Timer(TimerCallback callback, object? state, int dueTime, int period)
    { Initialize(callback, state, Timeout.Validate(dueTime, nameof(dueTime)), Timeout.Validate(period, nameof(period))); }
    public Timer(TimerCallback callback, object? state, long dueTime, long period)
    {
        // Preserve the Int64 overload's lower-bound checks before its upper-bound checks.
        if (dueTime < -1) throw new ArgumentOutOfRangeException(nameof(dueTime));
        if (period < -1) throw new ArgumentOutOfRangeException(nameof(period));
        Initialize(callback, state, Timeout.Validate(dueTime, nameof(dueTime)), Timeout.Validate(period, nameof(period)));
    }
    public Timer(TimerCallback callback, object? state, uint dueTime, uint period)
    { Initialize(callback, state, Timeout.Unsigned(dueTime), Timeout.Unsigned(period)); }
    public Timer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    { Initialize(callback, state, Timeout.Validate(dueTime, nameof(dueTime)), Timeout.Validate(period, nameof(period))); }
    private void Initialize(TimerCallback callback, object? state, long dueTime, long period)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        _callback = callback; _state = state; _period = period;
        _timer = new HostTimer(dueTime, Fire);
    }
    private void Fire()
    {
        if (_disposed) return;
        var callback = _callback!; var state = _state;
        _running++;
        try
        {
            // Rearm before user code so reentrant Change/Dispose wins. Missed ticks coalesce.
            _timer!.Change(_period > 0 ? _period : -1);
            callback(state);
        }
        finally
        {
            _running--;
            if (_running == 0 && _disposed) _drained?.TrySetResult(true);
        }
    }
    private bool ChangeCore(long dueTime, long period)
    {
        if (_disposed) return false;
        _timer!.Change(dueTime); _period = period; return true;
    }
    public bool Change(int dueTime, int period) => ChangeCore(Timeout.Validate(dueTime, nameof(dueTime)), Timeout.Validate(period, nameof(period)));
    public bool Change(long dueTime, long period)
    {
        if (dueTime < -1) throw new ArgumentOutOfRangeException(nameof(dueTime));
        if (period < -1) throw new ArgumentOutOfRangeException(nameof(period));
        return ChangeCore(Timeout.Validate(dueTime, nameof(dueTime)), Timeout.Validate(period, nameof(period)));
    }
    public bool Change(uint dueTime, uint period) => ChangeCore(Timeout.Unsigned(dueTime), Timeout.Unsigned(period));
    public bool Change(TimeSpan dueTime, TimeSpan period) => ChangeCore(Timeout.Validate(dueTime, nameof(dueTime)), Timeout.Validate(period, nameof(period)));
    internal bool TryReset()
    {
        if (_disposed || _timer == null || !_timer.TryReset()) return false;
        // HostTimer.TryReset retires its handle. The CTS creates another Timer when reused.
        _timer = null; _callback = null; _state = null; _disposed = true;
        return true;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var timer = _timer; _timer = null;
        _callback = null; _state = null;
        timer?.Dispose();
    }
    public ValueTask DisposeAsync()
    {
        Dispose();
        if (_running == 0) return default;
        _drained ??= new TaskCompletionSource<bool>();
        return new ValueTask(_drained.Task);
    }
}
