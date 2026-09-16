using System;
using System.Collections.Generic;
using System.Linq;

public static class Program
{
    public static void Main()
    {
        // List and LINQ algorithms are translated from the portable C# BCL implementation.
        List<int> squares = Enumerable.Range(1, 10)
            .Where(value => value % 2 == 0)
            .Select(value => value * value)
            .ToList();
        Console.WriteLine(squares.Sum()); // 220

        // This overload is imported from the selected original .NET CoreLib implementation IL.
        Console.WriteLine(Math.BigMul(int.MaxValue, int.MaxValue)); // 4611686014132420609
    }
}
