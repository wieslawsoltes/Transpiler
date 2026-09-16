using System;

public static class Program
{
    public static long Fibonacci(int n)
    {
        long a = 0, b = 1;
        for (int i = 0; i < n; i++) { long next = a + b; a = b; b = next; }
        return a;
    }

    public static void Main(string[] args)
    {
        Console.WriteLine("Roslyn -> CIL -> JavaScript / Python");
        Console.WriteLine(Fibonacci(78));
        var values = new int[8];
        for (int i = 0; i < values.Length; i++) values[i] = i * i;
        Console.WriteLine(values[7]);
        try { Console.WriteLine(values[99]); }
        catch (IndexOutOfRangeException) { Console.WriteLine("bounds checked"); }
        finally { Console.WriteLine("finally executed"); }
        if (args.Length != 0) Console.WriteLine(args[0]);
    }
}
