using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

public sealed class Source<T> : IValueTaskSource<T>, IValueTaskSource
{
    public ManualResetValueTaskSourceCore<T> Core;
    public int LastFlags;
    public int Consumed;
    public ValueTask<T> Value => new ValueTask<T>(this, Core.Version);
    public ValueTask VoidValue => new ValueTask(this, Core.Version);
    public ValueTaskSourceStatus GetStatus(short token) => Core.GetStatus(token);
    public T GetResult(short token) { Consumed++; return Core.GetResult(token); }
    void IValueTaskSource.GetResult(short token) { Consumed++; Core.GetResult(token); }
    public void OnCompleted(Action<object> action, object state, short token, ValueTaskSourceOnCompletedFlags flags)
    { LastFlags = (int)flags; Core.OnCompleted(action, state, token, flags); }
}
public struct Pair { public int X; }
public static class Program
{
    static async Task<int> Consume(Source<int> source) => await source.Value;
    static async Task<int> Configured(Source<int> source) => await source.Value.ConfigureAwait(false);
    public static async Task Main()
    {
        var source = new Source<int>();
        Console.WriteLine((int)source.Core.GetStatus(source.Core.Version));
        var pending = source.Value;
        Console.WriteLine(pending.IsCompleted);
        try { source.Core.GetResult(source.Core.Version); } catch (InvalidOperationException) { Console.WriteLine("pending checked"); }
        var consumed = Consume(source); source.Core.SetResult(42);
        Console.WriteLine(await consumed); Console.WriteLine(source.Consumed);
        source.Core.Reset();
        try { Console.WriteLine(pending.IsCompleted); } catch (InvalidOperationException) { Console.WriteLine("stale checked"); }
        var configured = Configured(source); Console.WriteLine(source.LastFlags); source.Core.SetResult(7); Console.WriteLine(await configured);
        source.Core.Reset();
        var task = source.Value.AsTask(); source.Core.SetResult(13); Console.WriteLine(await task); Console.WriteLine(await task);
        source.Core.Reset();
        var preserved = source.Value.Preserve(); source.Core.SetResult(19); Console.WriteLine(await preserved); Console.WriteLine(await preserved);
        source.Core.Reset();
        var e = new ArgumentException("source fault"); var failure = source.Value.AsTask(); source.Core.SetException(e);
        try { await failure; } catch (ArgumentException error) { Console.WriteLine(object.ReferenceEquals(e,error)); }
        source.Core.Reset();
        var token = new CancellationToken(true); var canceled = source.Value.AsTask(); source.Core.SetException(new OperationCanceledException(token));
        try { await canceled; } catch (OperationCanceledException error) { Console.WriteLine(error.CancellationToken == token); }
        Console.WriteLine(canceled.IsCanceled);
        source.Core.Reset();
        var voidTask = source.VoidValue.AsTask(); source.Core.SetResult(5); await voidTask; Console.WriteLine("void source completed");
        source.Core.Reset();
        source.Core.SetResult(23); Console.WriteLine(await source.Value.AsTask());
        source.Core.Reset();
        source.Core.RunContinuationsAsynchronously = true;
        var queued = Consume(source); source.Core.SetResult(31); Console.WriteLine(await queued);
        source.Core.Reset(); source.Core.RunContinuationsAsynchronously = false;
        int callbacks = 0;
        source.Core.OnCompleted(_ => { callbacks++; source.Core.Reset(); }, null, source.Core.Version, ValueTaskSourceOnCompletedFlags.None);
        source.Core.SetResult(9);
        Console.WriteLine(callbacks); Console.WriteLine((int)source.Core.GetStatus(source.Core.Version));
        source.Core.SetResult(11); var done = new TaskCompletionSource<int>();
        source.Core.OnCompleted(_ => done.SetResult(99), null, source.Core.Version, ValueTaskSourceOnCompletedFlags.None);
        Console.WriteLine(await done.Task);
        source.Core.Reset();
        Action action = () => callbacks++;
        source.Value.GetAwaiter().OnCompleted(action); Console.WriteLine(source.LastFlags); source.Core.SetResult(1);
        source.Core.Reset(); source.Value.GetAwaiter().UnsafeOnCompleted(action); Console.WriteLine(source.LastFlags); source.Core.SetResult(1);
        source.Core.Reset(); source.Value.ConfigureAwait(false).GetAwaiter().OnCompleted(action); Console.WriteLine(source.LastFlags); source.Core.SetResult(1);
        source.Core.Reset(); source.Value.ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted(action); Console.WriteLine(source.LastFlags); source.Core.SetResult(1);
        var pairs = new Source<Pair>(); var p = new Pair { X=17 }; pairs.Core.SetResult(p); p.X=88; Console.WriteLine((await pairs.Value).X);
        var texts = new Source<string>(); texts.Core.SetResult("source text"); Console.WriteLine(await texts.Value);
        try { new ValueTask<int>((IValueTaskSource<int>)null,0); } catch(ArgumentNullException) { Console.WriteLine("source null checked"); }
        Console.WriteLine("source protocol verified");
    }
}
