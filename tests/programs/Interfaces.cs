using System;
public interface ICounter { int Get(); void Add(int n); }
public interface IOther { int Get(); }
public struct Counter : ICounter { public int Value; public int Get()=>Value; public void Add(int n) { Value+=n; } }
public class Both : ICounter, IOther { public int Value; int ICounter.Get()=>Value; int IOther.Get()=>99; public void Add(int n) { Value+=n; } }
public static class Program
{
    static int Mutate<T>(ref T value) where T:ICounter { value.Add(5); return value.Get(); }
    public static void Main()
    {
        var counter=new Counter(); Console.WriteLine(Mutate(ref counter)); Console.WriteLine(counter.Value);
        ICounter box=counter; box.Add(7); Console.WriteLine(box.Get()); Console.WriteLine(counter.Value);
        var both=new Both(); ICounter a=both; IOther b=both; a.Add(3); Console.WriteLine(a.Get()); Console.WriteLine(b.Get());
        Console.WriteLine(both is ICounter);
    }
}
