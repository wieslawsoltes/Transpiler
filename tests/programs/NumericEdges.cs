using System;
using System.Runtime.CompilerServices;
public static class Program
{
    public static void Main()
    {
        Console.WriteLine(float.IsNegative(float.NaN)); Console.WriteLine(double.IsNegative(double.NaN));
        float negativeNaN = BitConverter.Int32BitsToSingle(unchecked((int)0xffc00001));
        Console.WriteLine(float.IsNegative(MathF.CopySign(3, negativeNaN)));
        try { RuntimeHelpers.InitializeArray(null, default); } catch (ArgumentNullException) { Console.WriteLine("null array validated"); }
        try { RuntimeHelpers.InitializeArray(new int[1], default); } catch (ArgumentException) { Console.WriteLine("invalid handle validated"); }
        Console.WriteLine(BitConverter.SingleToInt32Bits(float.Epsilon));
        Console.WriteLine(BitConverter.DoubleToInt64Bits(-0.0));
    }
}
