using System;
using System.Threading;
using System.Threading.Tasks;

public static class Program
{
    private static void Probe(Action action)
    {
        try { action(); Console.WriteLine("no error"); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine("range"); }
        catch (ObjectDisposedException) { Console.WriteLine("disposed"); }
    }
    public static void Main()
    {
        using var disposed = new CancellationTokenSource();
        disposed.Dispose();
        Probe(() => disposed.CancelAfter(-2));
        Probe(() => disposed.CancelAfter(-1));
        Probe(() => disposed.CancelAfter(0));
        Probe(() => disposed.TryReset());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Probe(() => canceled.CancelAfter(-2));
        canceled.CancelAfter(1);
        Console.WriteLine(canceled.IsCancellationRequested);
        Console.WriteLine(canceled.TryReset());
        Probe(() => Task.Delay(-2, new CancellationToken(true)));
        Probe(() => new CancellationTokenSource(-2));
        using var zero = new CancellationTokenSource(0);
        using var never = new CancellationTokenSource(-1);
        Console.WriteLine(zero.IsCancellationRequested);
        Console.WriteLine(never.IsCancellationRequested);
        never.CancelAfter(-1);
        Console.WriteLine(never.TryReset());
        Console.WriteLine(Task.Delay(0).IsCompletedSuccessfully);
        Console.WriteLine(Task.Delay(0, new CancellationToken(true)).IsCanceled);
        using var source = new CancellationTokenSource();
        var token = source.Token;
        var pending = Task.Delay(-1, token);
        Console.WriteLine(pending.IsCompleted);
        source.Cancel();
        Console.WriteLine(pending.IsCanceled);
        try { pending.GetAwaiter().GetResult(); }
        catch (OperationCanceledException error) { Console.WriteLine(error.CancellationToken == token); }
    }
}
