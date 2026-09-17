using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public static class TimerKernel
{
    private static CancellationTokenSource Source = new();
    private static Task Pending = Task.CompletedTask;
    private static int Callbacks;
    private static int Disposals;
    public static void Reset()
    {
        Source.Dispose(); Source = new CancellationTokenSource();
        Pending = Task.CompletedTask; Callbacks = 0;
        Source.Token.Register(() => Callbacks++);
    }
    public static object GetSource() => Source;
    public static void ScheduleCancel(int milliseconds) => Source.CancelAfter(milliseconds);
    public static bool TryResetSource() => Source.TryReset();
    public static bool IsCanceled() => Source.IsCancellationRequested;
    public static int CallbackCount() => Callbacks;
    public static void Close() => Source.Dispose();
    public static void ManualCancel() => Source.Cancel();
    public static void BeginDelay(int milliseconds) => Pending = Task.Delay(milliseconds, Source.Token);
    public static bool PendingCompleted() => Pending.IsCompleted;
    public static bool PendingCanceled() => Pending.IsCanceled;
    public static async Task<int> AwaitPending()
    {
        var token = Source.Token;
        try { await Pending; return 7; }
        catch (OperationCanceledException error) { return error.CancellationToken == token ? 9 : -1; }
    }
    public static async Task<int> YieldOne() { await Task.Yield(); return 1; }
    public static async Task<int> Delayed(int milliseconds) { await Task.Delay(milliseconds); return 42; }
    public static async Task<int> TimedCancel()
    {
        using var source = new CancellationTokenSource(10);
        try { await Task.Delay(-1, source.Token); return -1; }
        catch (OperationCanceledException error) { return error.CancellationToken == source.Token ? 9 : -2; }
    }
    public static int StressCancellation()
    {
        for (int i = 0; i < 128; i++)
        {
            using var source = new CancellationTokenSource();
            var task = Task.Delay(10000, source.Token);
            source.Cancel();
            if (!task.IsCanceled) return -1;
        }
        return 128;
    }
    public static int DisposedStreams() => Disposals;
    public static IAsyncEnumerable<int> Values(int milliseconds) => Iterate(milliseconds);
    private static async IAsyncEnumerable<int> Iterate(int milliseconds, [EnumeratorCancellation] CancellationToken token = default)
    {
        try
        {
            await Task.Delay(milliseconds, token);
            yield return 11;
            await Task.Delay(milliseconds, token);
            yield return 22;
        }
        finally { await Task.Delay(5); Disposals++; }
    }
}
