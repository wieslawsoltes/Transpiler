using System;
using System.Threading;
using System.Threading.Tasks;

public static class TimeKernel
{
    private static CancellationTokenSource Source = new();
    private static TaskCompletionSource<int> Input = new();
    private static Task<int> Wait = Task.FromResult(0);
    private static Timer? Timer;
    private static PeriodicTimer? Periodic;
    private static ValueTask<bool> Tick;
    private static ValueTask Disposal;
    private static int Calls;
    private static bool DisposeWasPending;
    private static InlineProvider Provider = new();
    public static void Reset()
    {
        Timer?.Dispose(); Periodic?.Dispose(); Source.Dispose();
        Timer = null; Periodic = null; Source = new(); Input = new(); Wait = Task.FromResult(0);
        Tick = default; Disposal = default; Calls = 0; DisposeWasPending = false; Provider = new();
    }
    public static async Task<int> Pump() { await Task.Yield(); return 1; }
    public static int Count() => Calls;
    public static object GetInput() => Input.Task;
    public static object GetSource() => Source;
    public static bool InputCompleted() => Input.Task.IsCompleted;
    public static void CompleteInput() => Input.SetResult(73);
    public static void FailInput() => Input.SetException(new InvalidOperationException("original"));
    public static void Cancel() => Source.Cancel();
    public static void StartWait(long ticks) => Wait = Input.Task.WaitAsync(TimeSpan.FromTicks(ticks), Source.Token);
    public static async Task<int> AwaitWait()
    {
        try { return await Wait; }
        catch (TimeoutException) { return -2; }
        catch (OperationCanceledException error) { return error.CancellationToken == Source.Token ? -3 : -4; }
        catch (InvalidOperationException error) { return error.Message == "original" ? -5 : -6; }
    }
    public static async Task<int> Delay(long milliseconds)
    { await Task.Delay(TimeSpan.FromMilliseconds(milliseconds), Source.Token); return 42; }
    public static async Task<int> TimedWait()
    {
        try { await Input.Task.WaitAsync(TimeSpan.FromMilliseconds(10)); return -1; }
        catch (TimeoutException) { return 9; }
    }
    public static async Task<int> DurationCancellation()
    {
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(10));
        try { await Task.Delay(Timeout.InfiniteTimeSpan, source.Token); return -1; }
        catch (OperationCanceledException error) { return error.CancellationToken == source.Token ? 11 : -2; }
    }
    public static void StartTimer(long dueTime, long period) => Timer = new Timer(_ => Calls++, null, dueTime, period);
    public static bool ChangeTimer(long dueTime, long period) => Timer!.Change(dueTime, period);
    public static void DisposeTimer() => Timer!.Dispose();
    public static async Task<bool> DrainTimer() { await Timer!.DisposeAsync(); return true; }
    public static void CallbackDisposal()
    {
        Timer = new Timer(_ =>
        {
            Calls++;
            Disposal = Timer!.DisposeAsync();
            DisposeWasPending = !Disposal.IsCompleted;
        }, null, 10, 10);
    }
    public static bool DisposalPendingInsideCallback() => DisposeWasPending;
    public static async ValueTask<bool> ObserveDisposal() { await Disposal; return true; }
    public static void CallbackChange()
    {
        Timer = new Timer(_ => { Calls++; if (Calls == 1) Timer!.Change(25, 0); }, null, 10, 10);
    }
    public static void CallbackOnlyConstructor()
    {
        Timer = new Timer(state => { if (object.ReferenceEquals(state, Timer)) Calls++; });
        Timer.Change(5, 0);
    }
    public static void StartPeriodic(long milliseconds) => Periodic = new PeriodicTimer(TimeSpan.FromMilliseconds(milliseconds));
    public static void ChangePeriod(long milliseconds) => Periodic!.Period = TimeSpan.FromMilliseconds(milliseconds);
    public static void BeginTick() => Tick = Periodic!.WaitForNextTickAsync(Source.Token);
    public static bool TickCompleted() => Tick.IsCompleted;
    public static async ValueTask<int> AwaitTick()
    {
        try { return await Tick ? 1 : 0; }
        catch (OperationCanceledException error) { return error.CancellationToken == Source.Token ? -3 : -4; }
    }
    public static void DisposePeriodic() => Periodic!.Dispose();
    public static void RenewToken() { Source.Dispose(); Source = new(); }
    public static async Task<int> NativePeriodic()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(3));
        int count = 0;
        while (count < 3 && await timer.WaitForNextTickAsync()) count++;
        return count;
    }
    public static long Timestamp() => TimeProvider.System.GetTimestamp();
    public static long TimestampFrequency() => TimeProvider.System.TimestampFrequency;
    public static void InlineDelay() { Provider.Mode = 1; Wait = ConvertDelay(Task.Delay(TimeSpan.FromMilliseconds(10), Provider)); }
    private static async Task<int> ConvertDelay(Task task) { await task; return 42; }
    public static bool InlineDelayDone() => Wait.IsCompletedSuccessfully;
    public static int ProviderDisposals() => Provider.Disposals;
    public static int ProviderCreations() => Provider.Creations;
    public static long ProviderDelayTicks() => Provider.DelayTicks;
    public static void InlineTimeout() { Provider.Mode = 1; Wait = Input.Task.WaitAsync(TimeSpan.FromTicks(109999), Provider, Source.Token); }
    public static void ProviderCompletesInput() { Provider.Mode = 2; Wait = Input.Task.WaitAsync(TimeSpan.FromMilliseconds(20), Provider); }
    public static bool ThrowingProvider()
    {
        Provider.Mode = 3;
        try { Input.Task.WaitAsync(TimeSpan.FromMilliseconds(10), Provider, Source.Token); return false; }
        catch (InvalidOperationException) { return true; }
    }
    public static void CustomCancellation()
    { Source.Dispose(); Provider.Mode = 0; Source = new CancellationTokenSource(TimeSpan.FromTicks(109999), Provider); }
    public static bool TryResetSource() => Source.TryReset();
    public static void ProviderFire() => Provider.Fire();
    public static bool SourceCanceled() => Source.IsCancellationRequested;
    public static void CloseSource() => Source.Dispose();
    public static bool NativeTimerProviderReset()
    {
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000), new ForwardProvider());
        return source.TryReset();
    }
    private sealed class ForwardProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new Timer(callback, state, dueTime, period);
    }
    private sealed class InlineProvider : TimeProvider
    {
        internal int Mode, Creations, Disposals;
        internal long DelayTicks;
        private TimerCallback? _callback;
        private object? _state;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Creations++; DelayTicks = dueTime.Ticks;
            if (Mode == 3) throw new InvalidOperationException("create failed");
            var timer = new ProbeTimer(this);
            _callback = callback; _state = state;
            if (Mode == 1) callback(state);
            else if (Mode == 2) Input.SetResult(73);
            return timer;
        }
        internal void Fire() => _callback?.Invoke(_state);
        private sealed class ProbeTimer : ITimer
        {
            private InlineProvider? _owner;
            internal ProbeTimer(InlineProvider owner) { _owner = owner; }
            public bool Change(TimeSpan dueTime, TimeSpan period) => _owner != null;
            public void Dispose()
            {
                var owner = _owner; _owner = null;
                if (owner == null) return;
                owner.Disposals++; owner._callback = null; owner._state = null;
            }
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }
}
