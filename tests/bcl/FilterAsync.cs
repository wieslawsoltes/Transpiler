using System;
using System.Collections.Generic;
using System.Threading.Tasks;
public static class Program
{
    static int State;
    static bool Test(string name) { Console.WriteLine(name); Console.WriteLine(State); return true; }
    static void Leaf() { try { State=1; throw new ArgumentException("call failure"); } finally { State=2; Console.WriteLine("callee cleanup"); } }
    static async Task Work()
    {
        await Task.Yield();
        try { Leaf(); } catch(ArgumentException e) when(Test("async local filter")) { Console.WriteLine(e.Message); }
        try { Action action=Leaf; action(); } catch(ArgumentException) when(Test("delegate filter")) { Console.WriteLine("delegate catch"); }
    }
    static IEnumerable<int> Values()
    {
        try { yield return 1; Leaf(); } finally { Console.WriteLine("iterator cleanup"); }
    }
    public static async Task Main()
    {
        await Work();
        try { foreach(var value in Values()) Console.WriteLine(value); }
        catch (ArgumentException) when (Test("iterator caller filter")) { Console.WriteLine("iterator catch"); }
        try { new List<int>{1,2,3}.ForEach(_ => Leaf()); }
        catch (ArgumentException) when (Test("library callback filter")) { Console.WriteLine("callback catch"); }
    }
}
