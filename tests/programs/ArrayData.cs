using System;
public enum Code : ushort { A=5,B=65535,C=130 }
public static class Program
{
    public static void Main()
    {
        int[] a={int.MinValue,-1,0,1,int.MaxValue,123456789}; foreach(int x in a)Console.WriteLine(x);
        long[] b={long.MinValue,-9007199254740993,0,9007199254740993,long.MaxValue}; foreach(long x in b)Console.WriteLine(x);
        ulong[] c={ulong.MaxValue,0,1,9223372036854775808}; foreach(ulong x in c)Console.WriteLine(x);
        byte[] d={0,1,127,128,254,255}; foreach(byte x in d)Console.WriteLine((int)x);
        short[] e={short.MinValue,-1,0,1,short.MaxValue}; foreach(short x in e)Console.WriteLine((int)x);
        float[] f={0.1f,-0.0f,float.MaxValue,float.Epsilon,1e-20f}; foreach(float x in f)Console.WriteLine(BitConverter.SingleToInt32Bits(x));
        double[] g={0.1,-0.0,double.MaxValue,double.Epsilon,1e-200}; foreach(double x in g)Console.WriteLine(BitConverter.DoubleToInt64Bits(x));
        Code[] h={Code.A,Code.B,Code.C,Code.A,Code.C}; foreach(Code x in h)Console.WriteLine((int)x);
        bool[] flags={true,false,true,true,false,true}; foreach(bool x in flags)Console.WriteLine(x);
        char[] chars={'A','\uD800','\uDC00','\uFFFF','Z'};foreach(char x in chars)Console.WriteLine((int)x);
        // Initialization storage is copied, not shared with mutable arrays.
        a[0]=42; int[] other={int.MinValue,-1,0,1,int.MaxValue,123456789};Console.WriteLine(other[0]);
    }
}
