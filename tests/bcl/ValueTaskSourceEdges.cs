using System;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

public sealed class Once : IValueTaskSource<int>
{
    public int Reads;
    public bool Fault;
    public bool Reset;
    public ManualResetValueTaskSourceCore<int> Core;
    public ValueTaskSourceStatus GetStatus(short token) => Fault ? ValueTaskSourceStatus.Faulted : Core.GetStatus(token);
    public int GetResult(short token)
    { Reads++; if(Fault) throw new OperationCanceledException(); int result=Core.GetResult(token); if(Reset) Core.Reset(); return result; }
    public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags)
        => Core.OnCompleted(continuation,state,token,flags);
}
public static class Program
{
    public static async Task Main()
    {
        var source=new Once(); source.Core.SetResult(44); source.Reset=true;
        var value=new ValueTask<int>(source,source.Core.Version); Console.WriteLine(value.ToString()); Console.WriteLine(source.Reads);
        source.Reset=false;
        var one=new ValueTask<int>(source,source.Core.Version); var same=new ValueTask<int>(source,source.Core.Version);
        Console.WriteLine(one==same); Console.WriteLine(one.GetHashCode()==same.GetHashCode()); source.Core.SetResult(1); Console.WriteLine(await one);
        source.Core.Reset(); Console.WriteLine(same==new ValueTask<int>(source,source.Core.Version));
        var fault=new Once{Fault=true}; var t=new ValueTask<int>(fault,0).AsTask();
        try{await t;}catch(OperationCanceledException){Console.WriteLine(t.IsFaulted);Console.WriteLine(t.IsCanceled);}
        var direct=new ManualResetValueTaskSourceCore<int>(); direct.Reset(); short stale=direct.Version; direct.Reset();
        try{direct.OnCompleted(_=>{},null,stale,ValueTaskSourceOnCompletedFlags.None);}catch(InvalidOperationException){Console.WriteLine("registration token checked");}
        direct.OnCompleted(_=>{},null,direct.Version,ValueTaskSourceOnCompletedFlags.None);
        try{direct.OnCompleted(_=>{},null,direct.Version,ValueTaskSourceOnCompletedFlags.None);}catch(InvalidOperationException){Console.WriteLine("single registration checked");}
        direct.SetResult(1); try{direct.SetResult(2);}catch(InvalidOperationException){Console.WriteLine("single completion checked");}
        direct.Reset(); try{direct.SetException(null);}catch(ArgumentNullException){Console.WriteLine("null fault checked");}
        for(int i=0;i<65536;i++) direct.Reset(); Console.WriteLine(direct.Version);
        Console.WriteLine("source edges verified");
    }
}
