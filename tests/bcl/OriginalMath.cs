using System;
public static class Program
{
    // Keep the calls in IL instead of allowing source constant folding.
    static int I(int x) => x;
    static long L(long x) => x;
    public static void Main()
    {
        Console.WriteLine(Math.BigMul(I(int.MinValue), I(int.MaxValue)));
        Console.WriteLine(Math.Min((sbyte)I(-128), (sbyte)I(127)));
        Console.WriteLine(Math.Max((sbyte)I(-128), (sbyte)I(127)));
        Console.WriteLine(Math.Min((byte)I(255), (byte)I(7)));
        Console.WriteLine(Math.Max((byte)I(255), (byte)I(7)));
        Console.WriteLine(Math.Min((short)I(-32768), (short)I(32767)));
        Console.WriteLine(Math.Max((short)I(-32768), (short)I(32767)));
        Console.WriteLine(Math.Min((ushort)I(65535), (ushort)I(5)));
        Console.WriteLine(Math.Max((ushort)I(65535), (ushort)I(5)));
        Console.WriteLine(Math.Min(I(int.MinValue), I(int.MaxValue)));
        Console.WriteLine(Math.Max(I(int.MinValue), I(int.MaxValue)));
        Console.WriteLine(Math.Min((uint)I(-1), (uint)I(1)));
        Console.WriteLine(Math.Max((uint)I(-1), (uint)I(1)));
        Console.WriteLine(Math.Min(L(long.MinValue), L(long.MaxValue)));
        Console.WriteLine(Math.Max(L(long.MinValue), L(long.MaxValue)));
        Console.WriteLine(Math.Min((ulong)L(-1), (ulong)L(long.MaxValue)));
        Console.WriteLine(Math.Max((ulong)L(-1), (ulong)L(long.MaxValue)));
        for (int a = -17; a <= 17; a += 17)
            for (int b = -5; b <= 5; b += 10)
            {
                Console.WriteLine(Math.DivRem(I(a), I(b), out int r)); Console.WriteLine(r);
                Console.WriteLine(Math.DivRem(L(a) * 9007199254740993L, L(b), out long r8)); Console.WriteLine(r8);
                Console.WriteLine(Math.Sign(I(a))); Console.WriteLine(Math.Sign(L(a)));
            }
        try { Math.DivRem(I(1), I(0), out _); } catch (DivideByZeroException) { Console.WriteLine("zero divisor"); }
        try { Math.DivRem(L(long.MinValue), L(-1), out _); } catch (OverflowException) { Console.WriteLine("overflow"); }
    }
}
