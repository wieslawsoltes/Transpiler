using System;
using System.Threading;
using System.Threading.Tasks;
public static class Program
{
    static async Task Canceled(CancellationToken token) { await Task.Yield(); token.ThrowIfCancellationRequested(); }
    static void Basics()
    {
        Console.WriteLine(CancellationToken.None.CanBeCanceled);Console.WriteLine(new CancellationToken(true).IsCancellationRequested);
        Console.WriteLine(new CancellationToken(false)==default(CancellationToken));
        using var source=new CancellationTokenSource();var token=source.Token;
        Console.WriteLine(token.CanBeCanceled);Console.WriteLine(token==source.Token);
        Console.WriteLine(token.Equals((object)source.Token));Console.WriteLine(token.GetHashCode()==source.Token.GetHashCode());
        token.Register(()=>Console.WriteLine("first"));
        var removed=token.Register(()=>Console.WriteLine("must not run"));removed.Dispose();
        token.Register(state=>Console.WriteLine((string)state),"second");
        token.Register((state,t)=>{Console.WriteLine(t==token);Console.WriteLine((string)state);},"third");
        var reg=token.Register(()=>{Console.WriteLine(source.IsCancellationRequested);source.Cancel();token.Register(()=>Console.WriteLine("inline late"));});
        Console.WriteLine(reg.Token==token);var copy=reg;Console.WriteLine(reg==copy);
        source.Cancel();source.Cancel();Console.WriteLine(reg.Unregister());
        try{token.ThrowIfCancellationRequested();}catch(OperationCanceledException e){Console.WriteLine(e.CancellationToken==token);}
        try{token.Register(()=>throw new ArgumentException("late"));}catch(ArgumentException e){Console.WriteLine(e.Message);}
        Console.WriteLine(source.TryReset());
        var reset=new CancellationTokenSource();reset.Token.Register(()=>Console.WriteLine("must not run reset"));Console.WriteLine(reset.TryReset());reset.Cancel();
        var disposed=new CancellationTokenSource();var saved=disposed.Token;disposed.Dispose();
        Console.WriteLine(saved.IsCancellationRequested);saved.Register(()=>Console.WriteLine("disposed callback"));
        try{disposed.Cancel();}catch(ObjectDisposedException){Console.WriteLine("disposed checked");}
        try{var t=disposed.Token;}catch(ObjectDisposedException){Console.WriteLine("token getter checked");}
    }
    static void Callbacks()
    {
        var s=new CancellationTokenSource();var token=s.Token;
        token.Register(()=>throw new ArgumentException("one"));token.Register(()=>throw new InvalidOperationException("two"));
        try{s.Cancel();}catch(AggregateException e){Console.WriteLine(e.InnerExceptions.Count);foreach(var error in e.InnerExceptions)Console.WriteLine(error.Message);}
        var first=new CancellationTokenSource();first.Token.Register(()=>Console.WriteLine("skipped"));first.Token.Register(()=>throw new Exception("first fault"));
        try{first.Cancel(true);}catch(Exception e){Console.WriteLine(e.Message);}first.Cancel();
        var during=new CancellationTokenSource();during.Token.Register(()=>Console.WriteLine("after dispose"));during.Token.Register(()=>during.Dispose());during.Cancel();
        var remove=new CancellationTokenSource();var r=remove.Token.Register(()=>Console.WriteLine("removed by callback"));remove.Token.Register(()=>r.Dispose());remove.Cancel();
        var parent=new CancellationTokenSource();var other=new CancellationTokenSource();
        var linked=CancellationTokenSource.CreateLinkedTokenSource(parent.Token,other.Token);linked.Token.Register(()=>Console.WriteLine("linked"));other.Cancel();Console.WriteLine(linked.IsCancellationRequested);
        var detached=CancellationTokenSource.CreateLinkedTokenSource(parent.Token);var dt=detached.Token;detached.Dispose();parent.Cancel();Console.WriteLine(dt.IsCancellationRequested);
        using var already=CancellationTokenSource.CreateLinkedTokenSource(new CancellationToken(true));Console.WriteLine(already.IsCancellationRequested);
    }
    public static async Task Main()
    {
        Basics();Callbacks();using var cts=new CancellationTokenSource();var token=cts.Token;cts.Cancel();
        var task=Canceled(token);try{await task;}catch(OperationCanceledException e){Console.WriteLine(e.CancellationToken==token);}Console.WriteLine(task.IsCanceled);
        try{await Task.FromCanceled<int>(token);}catch(TaskCanceledException e){Console.WriteLine(e.CancellationToken==token);Console.WriteLine(e.Task!=null);}
        try{Task.FromCanceled(default);}catch(ArgumentOutOfRangeException){Console.WriteLine("factory requires cancellation");}
        var canceledSource=new TaskCompletionSource<int>();canceledSource.SetCanceled(token);
        try{await canceledSource.Task;}catch(OperationCanceledException e){Console.WriteLine(e.CancellationToken==token);}Console.WriteLine(canceledSource.TrySetCanceled(token));
        using var waitCancel=new CancellationTokenSource();var pending=new TaskCompletionSource<int>();var wait=pending.Task.WaitAsync(waitCancel.Token);waitCancel.Cancel();
        try{await wait;}catch(OperationCanceledException e){Console.WriteLine(e.CancellationToken==waitCancel.Token);}Console.WriteLine(pending.Task.IsCompleted);
        using var successToken=new CancellationTokenSource();var successSource=new TaskCompletionSource<int>();var success=successSource.Task.WaitAsync(successToken.Token);successSource.SetResult(42);Console.WriteLine(await success);successToken.Cancel();Console.WriteLine(success.IsCompletedSuccessfully);
        var ready=Task.FromResult(7);Console.WriteLine(object.ReferenceEquals(ready,ready.WaitAsync(token)));
        var nongeneric=new TaskCompletionSource<int>();Task wrapped=((Task)nongeneric.Task).WaitAsync(successToken.Token);try{await wrapped;}catch(OperationCanceledException){Console.WriteLine("nongeneric wait canceled");}
    }
}
