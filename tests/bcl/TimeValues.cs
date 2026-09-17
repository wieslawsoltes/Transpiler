using System;
using System.Threading;
using System.Threading.Tasks;

public class ManualFrequency : TimeProvider
{
    public override long GetTimestamp() => 1000;
    public override long TimestampFrequency => 100;
}
public static class Program
{
    static void Probe(Action action)
    {
        try { action(); Console.WriteLine("ok"); }
        catch (ArgumentNullException) { Console.WriteLine("null"); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine("range"); }
        catch (ArgumentException) { Console.WriteLine("argument"); }
        catch (OverflowException) { Console.WriteLine("overflow"); }
        catch (ObjectDisposedException) { Console.WriteLine("disposed"); }
        catch (TimeoutException) { Console.WriteLine("timeout"); }
    }
    public static void Main()
    {
        var values = new long[] { 0, 1, -1, 9999, -9999, 10001, -19999, 9007199254740993L, long.MinValue, long.MaxValue };
        foreach (long ticks in values)
        {
            var value = TimeSpan.FromTicks(ticks);
            Console.WriteLine(value.Ticks); Console.WriteLine(value.Days); Console.WriteLine(value.Hours);
            Console.WriteLine(value.Minutes); Console.WriteLine(value.Seconds); Console.WriteLine(value.Milliseconds);
            Console.WriteLine(value.Microseconds); Console.WriteLine(value.Nanoseconds); Console.WriteLine(value.GetHashCode());
            Console.WriteLine(value == new TimeSpan(ticks));
        }
        Console.WriteLine(new TimeSpan(1, 2, 3).Ticks);
        Console.WriteLine(new TimeSpan(1, 2, 3, 4, 5).Ticks);
        Console.WriteLine(TimeSpan.FromMilliseconds(123L).Ticks);
        Console.WriteLine(TimeSpan.FromMilliseconds(0.12345).Ticks);
        Console.WriteLine(TimeSpan.FromMicroseconds(-123L).Ticks);
        Console.WriteLine(TimeSpan.FromDays(3).Ticks);
        Console.WriteLine((TimeSpan.FromHours(2) + TimeSpan.FromMinutes(3L)).Ticks);
        Console.WriteLine((TimeSpan.FromSeconds(10L) - TimeSpan.FromSeconds(2L)).Duration().Ticks);
        Console.WriteLine(new ManualFrequency().GetElapsedTime(200, 300).Ticks);
        Console.WriteLine(new ManualFrequency().GetElapsedTime(950).Ticks);
        Probe(() => TimeSpan.FromMilliseconds(double.NaN));
        Probe(() => TimeSpan.FromMilliseconds(double.PositiveInfinity));
        Probe(() => TimeSpan.FromSeconds(long.MaxValue));
        Probe(() => TimeSpan.MaxValue.Add(TimeSpan.FromTicks(1)));
        Probe(() => TimeSpan.MinValue.Negate());
        using var source = new CancellationTokenSource();
        var token = source.Token; source.Cancel();
        Console.WriteLine(Task.Delay(TimeSpan.FromMilliseconds(4294967294L), token).IsCanceled);
        Probe(() => Task.Delay(TimeSpan.FromMilliseconds(4294967295L), token));
        Probe(() => Task.Delay(TimeSpan.MinValue, (TimeProvider)null!, token));
        Console.WriteLine(Task.Delay(TimeSpan.FromTicks(-9999)).IsCompletedSuccessfully);
        Console.WriteLine(Task.Delay(TimeSpan.FromTicks(-19999), token).IsCanceled);
        Probe(() => Task.Delay(TimeSpan.FromTicks(-20000), token));
        var pending = new TaskCompletionSource<int>().Task;
        Probe(() => pending.WaitAsync(TimeSpan.Zero).GetAwaiter().GetResult());
        Console.WriteLine(object.ReferenceEquals(pending, pending.WaitAsync(Timeout.InfiniteTimeSpan)));
        var completed = Task.FromResult(42);
        Console.WriteLine(object.ReferenceEquals(completed, completed.WaitAsync(TimeSpan.Zero, token)));
        Probe(() => completed.WaitAsync(TimeSpan.MinValue));
        Probe(() => completed.WaitAsync(TimeSpan.MinValue, (TimeProvider)null!));
        using var timer = new Timer(_ => {});
        Console.WriteLine(timer.Change(-1L, 4294967294L));
        timer.Dispose();
        Probe(() => timer.Change(-2, 0));
        Probe(() => timer.Change(0, 0));
        Probe(() => new Timer(null!, null, -2, -2));
        Probe(() => new Timer(null!, null, 0, 0));
        source.Dispose();
        Probe(() => source.CancelAfter(TimeSpan.MinValue));
        Probe(() => source.CancelAfter(TimeSpan.Zero));
        using var periodic = new PeriodicTimer(Timeout.InfiniteTimeSpan);
        Console.WriteLine(periodic.Period == Timeout.InfiniteTimeSpan);
        periodic.Dispose();
        Console.WriteLine(periodic.WaitForNextTickAsync().GetAwaiter().GetResult());
        Probe(() => periodic.Period = TimeSpan.Zero);
        Probe(() => periodic.Period = TimeSpan.FromMilliseconds(1));
    }
}
