using System;
using System.Threading;
using System.Threading.Tasks;
public static class Program
{
    static async ValueTask<int> Throw(CancellationToken token) { await Task.Yield();throw new OperationCanceledException("value canceled",token); }
    static async Task<long> Forward(Task<long> task) => await task;
    public static async Task Main()
    {
        using var canceled=new CancellationTokenSource();canceled.Cancel();var token=canceled.Token;
        var inner=new Exception("inner");var exception=new OperationCanceledException("outer",inner,token);
        Console.WriteLine(exception.CancellationToken==token);Console.WriteLine(object.ReferenceEquals(exception.InnerException,inner));Console.WriteLine(exception.Message);
        var all=Task.WhenAll(new[] { Task.FromCanceled<int>(token),Task.FromResult(42) });
        try{await all;}catch(OperationCanceledException e){Console.WriteLine(e.CancellationToken==token);}
        using var uncanceled=new CancellationTokenSource();var a=new TaskCompletionSource<long>();var b=new TaskCompletionSource<long>();
        var combined=Task.WhenAll(new[] { a.Task,b.Task });var waiting=combined.WaitAsync(uncanceled.Token);
        a.SetCanceled(token);b.SetResult(42);
        try{await waiting;}catch(OperationCanceledException e){Console.WriteLine(e.CancellationToken==token);}Console.WriteLine(waiting.IsCanceled);
        try{await Forward(Task.FromCanceled<long>(token));}catch(OperationCanceledException e){Console.WriteLine(e.CancellationToken==token);}
        try{await Throw(token);}catch(OperationCanceledException e){Console.WriteLine(e.CancellationToken==token);Console.WriteLine(e.Message);}
        var value=ValueTask.FromCanceled<int>(token);Console.WriteLine(value.IsCanceled);try{await value;}catch(TaskCanceledException e){Console.WriteLine(e.CancellationToken==token);}
        var withoutToken=new TaskCompletionSource<int>();withoutToken.SetCanceled();try{await withoutToken.Task;}catch(TaskCanceledException e){Console.WriteLine(object.ReferenceEquals(e.Task,withoutToken.Task));Console.WriteLine(e.CancellationToken==default);}
        var source=new CancellationTokenSource();var registration=source.Token.Register(()=>Console.WriteLine("unregistered must not run"));await registration.DisposeAsync();source.Cancel();
        var error=Task.FromException<int>(exception);Console.WriteLine(error.IsFaulted);try{await error;}catch(OperationCanceledException e){Console.WriteLine(object.ReferenceEquals(exception,e));}
        var faultedAll=Task.WhenAll(new[] { Task.FromCanceled<int>(token),error });Console.WriteLine(faultedAll.IsFaulted);try{await faultedAll;}catch(OperationCanceledException e){Console.WriteLine(object.ReferenceEquals(e,exception));}
    }
}
