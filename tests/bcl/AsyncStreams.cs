using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public sealed class Resource : IAsyncDisposable
{
    public bool Disposed;
    public async ValueTask DisposeAsync() { await Task.Yield(); Disposed = true; Console.WriteLine("resource disposed"); }
}
public static class Program
{
    static int Disposed;
    static async IAsyncEnumerable<int> Range(int count, [EnumeratorCancellation] CancellationToken token = default)
    {
        try
        {
            for (int i=0; i<count; i++) { token.ThrowIfCancellationRequested(); await Task.Yield(); token.ThrowIfCancellationRequested(); yield return i; }
        }
        finally { await Task.Yield(); Disposed++; Console.WriteLine("iterator disposed"); }
    }
    static async IAsyncEnumerable<string> Text() { await Task.Yield(); yield return "alpha"; yield return "beta"; }
    static async IAsyncEnumerable<int> Failure()
    { try { yield return 7; await Task.Yield(); throw new ArgumentException("iterator fault"); } finally { Console.WriteLine("fault cleanup"); } }
    static async IAsyncEnumerable<int> DisposeFailure()
    { try { await Task.Yield(); yield return 1; } finally { await Task.Yield(); throw new InvalidOperationException("dispose fault"); } }
    static async IAsyncEnumerable<T> Echo<T>(T value) { await Task.Yield(); yield return value; }
    public static async Task Main()
    {
        var range = Range(4);
        int sum = 0; await foreach (int value in range) sum += value;
        Console.WriteLine(sum); Console.WriteLine(Disposed);
        await foreach (int value in range.ConfigureAwait(false)) { Console.WriteLine(value); if (value == 1) break; }
        Console.WriteLine(Disposed);
        var enumerator = Range(1).GetAsyncEnumerator();
        Console.WriteLine(await enumerator.MoveNextAsync()); Console.WriteLine(enumerator.Current);
        Console.WriteLine(await enumerator.MoveNextAsync()); await enumerator.DisposeAsync();
        Console.WriteLine(await enumerator.MoveNextAsync());
        var unused = Range(2).GetAsyncEnumerator(); await unused.DisposeAsync(); Console.WriteLine(Disposed);
        try { await foreach (int value in Failure()) Console.WriteLine(value); }
        catch (ArgumentException error) { Console.WriteLine(error.Message); }
        try { await foreach (int value in DisposeFailure()) break; }
        catch (InvalidOperationException error) { Console.WriteLine(error.Message); }
        using var cancel = new CancellationTokenSource();
        try
        {
            await foreach (int value in Range(5).WithCancellation(cancel.Token).ConfigureAwait(false))
            { Console.WriteLine(value); cancel.Cancel(); }
        }
        catch (OperationCanceledException error) { Console.WriteLine(error.CancellationToken == cancel.Token); }
        using var first = new CancellationTokenSource(); using var second = new CancellationTokenSource();
        try
        {
            await foreach (int value in Range(5, first.Token).ConfigureAwait(false).WithCancellation(second.Token))
            { Console.WriteLine(value); first.Cancel(); }
        }
        catch (OperationCanceledException error)
        { Console.WriteLine(error.CancellationToken.IsCancellationRequested); Console.WriteLine(error.CancellationToken != first.Token); }
        IAsyncEnumerable<object> objects = Text();
        await foreach (var value in objects) Console.WriteLine(value);
        await foreach (var value in Echo(9007199254740993L)) Console.WriteLine(value);
        var resource = new Resource(); await using (resource.ConfigureAwait(false)) Console.WriteLine("resource body");
        Console.WriteLine(resource.Disposed);
        Console.WriteLine("async streams verified");
    }
}
