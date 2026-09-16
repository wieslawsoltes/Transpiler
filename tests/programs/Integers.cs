using System;
public static class Program
{
    static int I(int value) => value;
    static long L(long value) => value;
    static uint U(uint value) => value;
    static ulong UL(ulong value) => value;
    public static void Main()
    {
        int a = I(int.MaxValue), b = I(2), n = I(-17);
        Console.WriteLine(unchecked(a + b));
        Console.WriteLine(unchecked(a * a));
        Console.WriteLine(n / 5); Console.WriteLine(n % 5);
        Console.WriteLine(n >> 3); Console.WriteLine(n >>> 3);
        Console.WriteLine(1 << I(35)); Console.WriteLine(~n);
        try { Console.WriteLine(checked(a + b)); } catch (OverflowException) { Console.WriteLine("i4 overflow"); }
        try { Console.WriteLine(I(int.MinValue) / I(-1)); } catch (OverflowException) { Console.WriteLine("division overflow"); }
        Console.WriteLine(I(int.MinValue) % I(-1));
        try { Console.WriteLine(n / I(0)); } catch (DivideByZeroException) { Console.WriteLine("zero"); }
        uint u = U(uint.MaxValue);
        Console.WriteLine(u / 3); Console.WriteLine(u > U(2));
        Console.WriteLine(unchecked(u + U(2))); Console.WriteLine((long)u);
        long large = L(9007199254740993L);
        Console.WriteLine(large + 2); Console.WriteLine(large * 13);
        Console.WriteLine(unchecked(L(long.MaxValue) + L(1)));
        Console.WriteLine(L(-9007199254740993L) / L(7));
        Console.WriteLine(L(-9007199254740993L) % L(7));
        Console.WriteLine(L(-1) >>> 63);
        ulong big = UL(ulong.MaxValue);
        Console.WriteLine(big); Console.WriteLine(big / UL(11));
        Console.WriteLine(big > UL(9223372036854775808UL));
        try { Console.WriteLine(checked(big + UL(1))); } catch (OverflowException) { Console.WriteLine("u8 overflow"); }
        Console.WriteLine((byte)n); Console.WriteLine((short)u); Console.WriteLine((uint)large);
        try { Console.WriteLine(checked((byte)n)); } catch (OverflowException) { Console.WriteLine("conversion overflow"); }
        Console.WriteLine(checked((long)U(4000000000)));
        Console.WriteLine(Math.Abs(L(-44))); Console.WriteLine(Math.Max(large, L(7)));
    }
}
