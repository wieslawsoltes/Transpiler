using System;
using Transpiler.Bcl.Threading;
using Transpiler.Bcl.Tasks;

namespace Transpiler.Bcl;

/// <summary>Timer/timestamp portion of TimeProvider; calendar/time-zone APIs require other capabilities.</summary>
public abstract class TimeProvider
{
    public static TimeProvider System { get; } = new SystemProvider();
    protected TimeProvider() { }
    public virtual long TimestampFrequency => TimeSpan.TicksPerSecond;
    public virtual long GetTimestamp() => checked((long)(HostClock.Now() * TimeSpan.TicksPerMillisecond));
    public TimeSpan GetElapsedTime(long startingTimestamp) => GetElapsedTime(startingTimestamp, GetTimestamp());
    public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
    {
        long frequency = TimestampFrequency;
        if (frequency <= 0) throw new InvalidOperationException("TimestampFrequency must be positive.");
        return TimeSpan.FromTicks(checked((long)((endingTimestamp - startingTimestamp) * ((double)TimeSpan.TicksPerSecond / frequency))));
    }
    public virtual ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (callback == null) throw new ArgumentNullException(nameof(callback));
        return new ProviderTimer(callback, state, dueTime, period);
    }
    private sealed class SystemProvider : TimeProvider { }
    private sealed class ProviderTimer : ITimer
    {
        private readonly Timer _timer;
        internal ProviderTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { _timer = new Timer(callback, state, dueTime, period); }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            try { return _timer.Change(dueTime, period); }
            catch (ObjectDisposedException) { return false; }
        }
        public void Dispose() => _timer.Dispose();
        public ValueTask DisposeAsync() => _timer.DisposeAsync();
    }
}
