using System;
using Transpiler.Bcl.Threading;

namespace Transpiler.Bcl.Tasks;

public partial class Task
{
    public static Task Delay(int millisecondsDelay) => Delay(millisecondsDelay, default(CancellationToken));
    public static Task Delay(int millisecondsDelay, CancellationToken cancellationToken)
    {
        if (millisecondsDelay < -1) throw new ArgumentOutOfRangeException(nameof(millisecondsDelay));
        return DelayCore(millisecondsDelay, TimeProvider.System, cancellationToken);
    }
    public static Task Delay(TimeSpan delay) => Delay(delay, default(CancellationToken));
    public static Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        => DelayCore(Timeout.Validate(delay, nameof(delay)), TimeProvider.System, cancellationToken);
    public static Task Delay(TimeSpan delay, TimeProvider timeProvider) => Delay(delay, timeProvider, default);
    public static Task Delay(TimeSpan delay, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (timeProvider == null) throw new ArgumentNullException(nameof(timeProvider));
        return DelayCore(Timeout.Validate(delay, nameof(delay)), timeProvider, cancellationToken);
    }
    private static Task DelayCore(long milliseconds, TimeProvider provider, CancellationToken token)
    {
        if (token.IsCancellationRequested) return FromCanceled(token);
        if (milliseconds == 0) return CompletedTask;
        return new DelayPromise(milliseconds, provider, token);
    }
    private sealed class DelayPromise : Task
    {
        private ITimer? _timer;
        private CancellationTokenRegistration _registration;
        private CancellationToken _token;
        internal DelayPromise(long milliseconds, TimeProvider provider, CancellationToken token)
        {
            _token = token;
            try
            {
                if (milliseconds != -1)
                {
                    var timer = provider.CreateTimer(_ => CompleteDelay(), null, TimeSpan.FromMilliseconds(milliseconds), Timeout.InfiniteTimeSpan);
                    _timer = timer;
                    // A custom provider may invoke synchronously before returning its timer.
                    if (IsCompleted) { timer.Dispose(); _timer = null; }
                }
                if (token.CanBeCanceled && !IsCompleted)
                {
                    var registration = token.Register(CancelDelay);
                    _registration = registration;
                    if (IsCompleted) { registration.Dispose(); _registration = default; }
                }
            }
            catch { Cleanup(); throw; }
        }
        private void Cleanup()
        {
            var timer = _timer; _timer = null;
            _registration.Dispose(); _registration = default; _token = default;
            timer?.Dispose();
        }
        private void CompleteDelay()
        {
            if (IsCompleted) return;
            try { Finish(1, null); } finally { Cleanup(); }
        }
        private void CancelDelay()
        {
            if (IsCompleted) return;
            var token = _token;
            try { Cancel(token); } finally { Cleanup(); }
        }
    }
}
