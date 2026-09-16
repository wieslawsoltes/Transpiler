using System;
public static class Program
{
    static double D(double x) => x;
    public static void Main()
    {
        double zero = D(0), negativeZero = D(-0.0), nan = D(double.NaN), a = D(-17.5);
        Console.WriteLine(a / D(4)); Console.WriteLine(a % D(4));
        Console.WriteLine(D(1) / zero); Console.WriteLine(D(1) / negativeZero);
        Console.WriteLine(double.IsNaN(zero / zero)); Console.WriteLine(double.IsInfinity(D(1) / zero));
        Console.WriteLine(nan < D(3)); Console.WriteLine(nan >= D(3)); Console.WriteLine(nan != nan);
        Console.WriteLine(Math.Sqrt(D(81))); Console.WriteLine(Math.Sqrt(D(-1)));
        Console.WriteLine(Math.Floor(D(-1.2))); Console.WriteLine(Math.Ceiling(D(-0.2))); Console.WriteLine(Math.Truncate(D(-0.2)));
        Console.WriteLine(Math.Min(zero, negativeZero)); Console.WriteLine(Math.Max(zero, negativeZero));
        Console.WriteLine(D(1e-5)); Console.WriteLine(D(1e16)); Console.WriteLine(D(1e17));
        Console.WriteLine(checked((int)D(-123.75)));
        try { Console.WriteLine(checked((long)nan)); } catch (OverflowException) { Console.WriteLine("NaN conversion checked"); }
        Console.WriteLine((double)9007199254740993L);
    }
}
