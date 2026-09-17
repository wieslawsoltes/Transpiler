using System;
using System.Threading;
using System.Threading.Tasks;

// Compile with --library --bcl portable, then call through invokeAsync / invoke_async.
public static class HostTimers
{
    public static async Task<int> Delayed(int milliseconds)
    {
        await Task.Delay(milliseconds);
        return 42;
    }

    public static async Task<bool> CancellationIdentity(int milliseconds)
    {
        using var source = new CancellationTokenSource(milliseconds);
        try { await Task.Delay(-1, source.Token); return false; }
        catch (OperationCanceledException error) { return error.CancellationToken == source.Token; }
    }
}
