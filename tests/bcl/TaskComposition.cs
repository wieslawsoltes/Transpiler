using System;
using System.Collections.Generic;
using System.Threading.Tasks;
public static class Program
{
    static void Check(bool value) { if (!value) throw new InvalidOperationException("Task contract failed."); }
    static IEnumerable<Task<int>> Items(Task<int> first, Task<int> second) { yield return first; yield return second; }
    static async Task<int> Double(Task<int> task) { int value = await task; await Task.Yield(); return value * 2; }
    public static async Task Main()
    {
        var a = new TaskCompletionSource<int>(); var b = new TaskCompletionSource<int>();
        Task<int>[] input = { a.Task, b.Task, a.Task };
        Task<int[]> all = Task.WhenAll(input); input[0] = Task.FromResult(999);
        Check(!all.IsCompleted); b.SetResult(7); Check(!all.IsCompleted); a.SetResult(3);
        Check(all.IsCompletedSuccessfully);
        int[] results = await all; foreach (int value in results) Console.WriteLine(value);
        var completed = Task.WhenAll(new Task[0]); Check(completed.IsCompletedSuccessfully);
        Check((await Task.WhenAll(new Task<int>[0])).Length == 0);
        var x = new TaskCompletionSource<int>(); var y = new TaskCompletionSource<int>();
        var first = Task.WhenAny(new[] { x.Task, y.Task });
        y.SetException(new ArgumentException("winner fault"));
        Check(first.IsCompletedSuccessfully); Check(object.ReferenceEquals(await first, y.Task));
        try { await await first; } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        Check(!x.Task.IsCompleted); x.SetResult(9);
        Check(object.ReferenceEquals(await first, y.Task));
        var canceled = new TaskCompletionSource<int>(); canceled.SetCanceled();
        var pending = new TaskCompletionSource<int>();
        var canceledAll = Task.WhenAll(new[] { canceled.Task, pending.Task });
        Check(!canceledAll.IsCompleted); pending.SetResult(0); Check(canceledAll.IsCanceled);
        try { await canceledAll; } catch (OperationCanceledException) { Console.WriteLine("all canceled"); }
        var e1 = new InvalidOperationException("first fault"); var e2 = new ArgumentException("second fault");
        Task<int[]> faulty = Task.WhenAll(new[] { Task.FromException<int>(e1), canceled.Task, Task.FromException<int>(e2) });
        Check(faulty.IsFaulted && faulty.Exception.InnerExceptions.Count == 2);
        Check(object.ReferenceEquals(faulty.Exception.InnerExceptions[0], e1));
        Check(object.ReferenceEquals(faulty.Exception.InnerExceptions[1], e2));
        try { await faulty; } catch (InvalidOperationException error) { Check(object.ReferenceEquals(error, e1)); Console.WriteLine(error.Message); }
        var ordered = await Task.WhenAll(Items(Task.FromResult(8), Task.FromResult(4)));
        Console.WriteLine(ordered[0]); Console.WriteLine(ordered[1]);
        Task anyCanceled = await Task.WhenAny((Task)canceled.Task, (Task)Task.FromResult(5)); Check(anyCanceled.IsCanceled);
        var delayed = new TaskCompletionSource<int>();
        var composed = Task.WhenAll(new[] { Double(delayed.Task), Double(Task.FromResult(2)) });
        delayed.SetResult(6); int[] doubled = await composed; Console.WriteLine(doubled[0]+doubled[1]);
        try { Task.WhenAll((Task[])null); } catch (ArgumentNullException) { Console.WriteLine("null checked"); }
        try { Task.WhenAny(new Task[0]); } catch (ArgumentException) { Console.WriteLine("empty checked"); }
        try { Task.WhenAll(new Task[] { Task.CompletedTask, null }); } catch (ArgumentException) { Console.WriteLine("element checked"); }
        try { Task.WhenAny(Task.CompletedTask, (Task)null); } catch (ArgumentNullException) { Console.WriteLine("pair checked"); }
        var multiple = new TaskCompletionSource<int>();
        multiple.SetException(new Exception[] { e1, e2 });
        Check(multiple.Task.Exception.InnerExceptions.Count == 2);
        try { int ignored = multiple.Task.Result; } catch (AggregateException errors) { Console.WriteLine(errors.InnerExceptions.Count); }
        Check(Task.FromResult(123).Result == 123); Task.CompletedTask.Wait();
        try { canceled.Task.Wait(); } catch (AggregateException errors) { Check(errors.InnerExceptions[0] is OperationCanceledException); Console.WriteLine("wait wraps cancellation"); }
        Console.WriteLine("task composition verified");
    }
}
