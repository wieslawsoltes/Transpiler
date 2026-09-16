using System;
using System.Threading.Tasks;
public struct Payload { public int Value; }
public static class Program
{
    static async ValueTask<long> Calculate(int n)
    { long result=9007199254740993L; for(int i=0;i<n;i++) { await Task.Yield(); result+=i; } return result; }
    static async ValueTask Finish() { await Task.Yield(); Console.WriteLine("void value task"); }
    static async ValueTask Fail() { try { await Task.Yield(); throw new InvalidOperationException("value fault"); } finally { Console.WriteLine("value finally"); } }
    static async Task Consume(Task<int> pending) { Console.WriteLine(await new ValueTask<int>(pending).ConfigureAwait(false)); }
    public static async Task Main()
    {
        Console.WriteLine(default(ValueTask).IsCompletedSuccessfully);
        Console.WriteLine(default(ValueTask<int>).Result);
        Console.WriteLine(await Calculate(5).ConfigureAwait(false)); await Finish();
        var value=new ValueTask<int>(42); Console.WriteLine(value.IsCompleted);Console.WriteLine(value.IsCanceled);Console.WriteLine(value.IsFaulted);
        Console.WriteLine(value==ValueTask.FromResult(42));Console.WriteLine(value.Equals((object)new ValueTask<int>(42)));
        Console.WriteLine(value.ToString()); Console.WriteLine(await value.Preserve().AsTask());
        var original=Task.FromResult(7);var wrapped=new ValueTask<int>(original);
        Console.WriteLine(object.ReferenceEquals(original,wrapped.AsTask()));
        var source=new TaskCompletionSource<int>();var waiting=Consume(source.Task);source.SetResult(99);await waiting;
        try { await Fail(); } catch(InvalidOperationException e) { Console.WriteLine(e.Message); }
        try { await ValueTask.FromException<int>(new ArgumentException("fault factory")); } catch(ArgumentException e) {Console.WriteLine(e.Message);}
        var canceled=new TaskCompletionSource<int>();canceled.SetCanceled();var cv=new ValueTask<int>(canceled.Task);
        Console.WriteLine(cv.IsCanceled);try{await cv;}catch(OperationCanceledException){Console.WriteLine("value canceled");}
        var p=new Payload{Value=3};var v=new ValueTask<Payload>(p);p.Value=8;var copy=await v;copy.Value=9;Console.WriteLine(v.Result.Value);
        try{new ValueTask((Task)null);}catch(ArgumentNullException){Console.WriteLine("null task checked");}
    }
}
