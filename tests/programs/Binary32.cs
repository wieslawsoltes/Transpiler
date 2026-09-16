using System;
public class Cell { public float Value; }
public static class Program
{
    static float F(float x) => x;
    static void Add(ref float x) { x += F(0.1f); }
    static void Bits(float x) => Console.WriteLine(BitConverter.SingleToInt32Bits(x));
    public static void Main()
    {
        float a=F(16777216f), b=F(1f);
        Bits((a+b)-a); Bits(a+(b-a));
        Bits(F(float.MaxValue)*F(2)); Bits(F(float.Epsilon)/F(2));
        Bits(F(1f)/F(3)); Bits(F(-17.5f)%F(4));
        float sum=0; for(int i=0;i<100;i++) sum+=F(0.1f); Bits(sum);
        var c=new Cell(); c.Value=F(0.1f); Add(ref c.Value); Bits(c.Value);
        float[] values=new float[3]; values[1]=sum; Add(ref values[1]); Bits(values[1]);
        long midpoint=(1L<<62)+(1L<<38)+1;
        Bits((float)midpoint); Bits((float)(-midpoint)); Bits((float)(ulong)midpoint);
        double large=16777217; Bits((float)large); Console.WriteLine((double)sum);
        float negzero=BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        Bits(negzero); Console.WriteLine(float.IsNegative(negzero));
        Console.WriteLine(float.IsFinite(sum)); Console.WriteLine(float.IsNaN(F(0)/F(0)));
        Console.WriteLine(float.NaN.Equals(float.NaN)); Console.WriteLine(float.NaN.CompareTo(1f));
        Bits(MathF.Sqrt(F(2))); Bits(MathF.Min(F(0),negzero)); Bits(MathF.Max(F(0),negzero));
        Bits(MathF.CopySign(F(3),negzero)); Bits(MathF.Truncate(F(-0.2f)));
        Console.WriteLine(F(0.1f)); Console.WriteLine(F(1e8f)); Console.WriteLine(F(1e9f));
        Console.WriteLine(F(1e-4f)); Console.WriteLine(F(1e-5f)); Console.WriteLine(negzero);
        object box=sum; Bits((float)box); Console.WriteLine(box);
        Console.WriteLine(checked((int)F(-12.9f)));
        try { Console.WriteLine(checked((int)F(float.NaN))); } catch(OverflowException) {Console.WriteLine("checked nan");}
    }
}
