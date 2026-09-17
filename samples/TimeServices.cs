using System;
using System.Threading;
using System.Threading.Tasks;

// Compile as a library with --bcl portable. Native timers need asynchronous host entry points.
public static class TimeServices
{
    public static async Task<int> Delayed(int milliseconds)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(milliseconds), TimeProvider.System);
        return 42;
    }

    public static async Task<bool> TimeoutWithoutCancelingInput()
    {
        var input = new TaskCompletionSource<int>();
        try
        {
            await input.Task.WaitAsync(TimeSpan.FromMilliseconds(10));
            return false;
        }
        catch (TimeoutException)
        {
            bool stillPending = !input.Task.IsCompleted;
            input.SetResult(73);
            return stillPending && await input.Task == 73;
        }
    }

    public static async Task<int> CountTicks(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(5));
        int ticks = 0;
        while (ticks < count && await timer.WaitForNextTickAsync()) ticks++;
        return ticks;
    }
}
