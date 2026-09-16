using System;
using System.Threading.Tasks;
public static class Program
{
    static async Task<long> Calculate(int count)
    {
        long sum=9007199254740993L;
        for(int i=0;i<count;i++){await Task.Yield();sum+=i;}
        sum+=await Task.FromResult(7).ConfigureAwait(false);
        return sum;
    }
    static async Task Fail()
    { try {await Task.Yield();throw new InvalidOperationException("async failure");} finally {Console.WriteLine("async finally");} }
    static async Task Recover()
    {try{await Fail();}catch(InvalidOperationException e){Console.WriteLine(e.Message);}await Task.Yield();Console.WriteLine("recovered");}
    static async Task<int> Consume(Task<int> input)
    { var result=await input;await Task.Yield();return result*2; }
    public static async Task Main()
    {
        Console.WriteLine(await Calculate(5));
        await Recover();
        var source=new TaskCompletionSource<int>();var waiting=Consume(source.Task);
        source.SetResult(21);Console.WriteLine(await waiting);
        Console.WriteLine(source.TrySetResult(0));Console.WriteLine(source.Task.IsCompletedSuccessfully);
        var canceled=new TaskCompletionSource<int>();canceled.SetCanceled();
        try{await canceled.Task;}catch(OperationCanceledException){Console.WriteLine("canceled");}
        Console.WriteLine(canceled.Task.IsCanceled);
        var failure=Task.FromException<int>(new ArgumentException("preserved"));
        try{await failure;}catch(ArgumentException e){Console.WriteLine(e.Message);}
        Console.WriteLine(Task.FromException(new OperationCanceledException()).IsFaulted);
        var faultSource=new TaskCompletionSource<int>();faultSource.SetException(new OperationCanceledException());Console.WriteLine(faultSource.Task.IsFaulted);
        Console.WriteLine(Task.CompletedTask is Task<int>);
        await Task.CompletedTask;Console.WriteLine("complete");
    }
}
