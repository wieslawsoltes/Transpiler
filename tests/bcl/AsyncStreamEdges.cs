using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

public struct Item { public int X; }
public static class Program
{
    static int Closed;
    static async IAsyncEnumerable<Item> Items([EnumeratorCancellation] CancellationToken token = default)
    {
        var item = new Item { X=10 };
        try { token.ThrowIfCancellationRequested(); yield return item; await Task.Yield(); item.X=20; token.ThrowIfCancellationRequested(); yield return item; }
        finally { await Task.Yield(); Closed++; }
    }
    static async IAsyncEnumerable<int> Throws(Exception error, Exception cleanup)
    {
        try { await Task.Yield(); yield return 1; throw error; }
        finally { await Task.Yield(); if (cleanup != null) throw cleanup; }
    }
    static async IAsyncEnumerable<int> Nested()
    { try { await foreach (var item in Items()) yield return item.X; } finally { await Task.Yield(); Closed+=10; } }
    public static async Task Main()
    {
        var iterator = Items().GetAsyncEnumerator();
        Console.WriteLine(await iterator.MoveNextAsync()); var copy=iterator.Current; copy.X=99; Console.WriteLine(iterator.Current.X);
        Console.WriteLine(await iterator.MoveNextAsync()); Console.WriteLine(iterator.Current.X); await iterator.DisposeAsync(); Console.WriteLine(Closed);
        var shared=Items(); var a=shared.GetAsyncEnumerator(); var b=shared.GetAsyncEnumerator();
        Console.WriteLine(object.ReferenceEquals(a,b)); Console.WriteLine(await a.MoveNextAsync()); Console.WriteLine(await b.MoveNextAsync());
        await a.DisposeAsync(); Console.WriteLine(await b.MoveNextAsync()); await b.DisposeAsync(); Console.WriteLine(Closed);
        await foreach (var value in Nested()) { Console.WriteLine(value); break; } Console.WriteLine(Closed);
        var original = new ArgumentException("original");
        try { await foreach(var value in Throws(original,null)) Console.WriteLine(value); }
        catch(ArgumentException error) { Console.WriteLine(object.ReferenceEquals(error,original)); }
        var replacement = new InvalidOperationException("replacement");
        try { await foreach(var value in Throws(original,replacement)) Console.WriteLine(value); }
        catch(InvalidOperationException error) { Console.WriteLine(object.ReferenceEquals(error,replacement)); }
        using var token = new CancellationTokenSource(); token.Cancel();
        try { await foreach(var item in Items(token.Token)) Console.WriteLine("unreachable"); }
        catch(OperationCanceledException error) { Console.WriteLine(error.CancellationToken==token.Token); }
        var capture=ExceptionDispatchInfo.Capture(original); Console.WriteLine(object.ReferenceEquals(capture.SourceException,original));
        try { capture.Throw(); } catch(ArgumentException error) { Console.WriteLine(object.ReferenceEquals(error,original)); }
        try { ExceptionDispatchInfo.Throw(original); } catch(ArgumentException error) { Console.WriteLine(object.ReferenceEquals(error,original)); }
        try { ExceptionDispatchInfo.Capture(null); } catch(ArgumentNullException) { Console.WriteLine("capture null checked"); }
        Console.WriteLine("stream edges verified");
    }
}
