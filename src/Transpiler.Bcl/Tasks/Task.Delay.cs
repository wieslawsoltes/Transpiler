using System;
using Transpiler.Bcl.Threading;

namespace Transpiler.Bcl.Tasks;

public partial class Task
{
    public static Task Delay(int millisecondsDelay) => Delay(millisecondsDelay, default);
    public static Task Delay(int millisecondsDelay, CancellationToken cancellationToken)
    {
        if (millisecondsDelay < -1) throw new ArgumentOutOfRangeException(nameof(millisecondsDelay));
        if (cancellationToken.IsCancellationRequested) return FromCanceled(cancellationToken);
        if (millisecondsDelay == 0) return CompletedTask;
        return new DelayPromise(millisecondsDelay, cancellationToken);
    }

    private sealed class DelayPromise : Task
    {
        private HostTimer? _timer;
        private CancellationTokenRegistration _registration;
        private CancellationToken _token;

        internal DelayPromise(int milliseconds, CancellationToken token)
        {
            _token = token;
            if (milliseconds != -1) _timer = new HostTimer(milliseconds, CompleteDelay);
            if (token.CanBeCanceled)
            {
                var registration = token.Register(CancelDelay);
                _registration = registration;
                // Register can invoke synchronously. Never retain its returned subscription then.
                if (IsCompleted) { registration.Dispose(); _registration = default; }
            }
        }

        private void Cleanup()
        {
            var timer = _timer; _timer = null;
            timer?.Dispose();
            _registration.Dispose(); _registration = default;
            _token = default;
        }
        private void CompleteDelay() { Cleanup(); Finish(1, null); }
        private void CancelDelay()
        {
            var token = _token;
            Cleanup();
            Cancel(token);
        }
    }
}
