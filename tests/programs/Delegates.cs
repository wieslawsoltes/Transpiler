using System;
public delegate long Compute(long value);
public class Accumulator
{
    public int Value;
    public virtual int Apply(int value) { Value+=value; return Value; }
}
public class DoubleAccumulator:Accumulator { public override int Apply(int value)=>base.Apply(value*2); }
public static class Program
{
    static int Twice(int value)=>value*2;
    static long Inc(long value)=>value+1;
    public static void Main()
    {
        Func<int,int> twice=Twice; Console.WriteLine(twice(21));
        int captured=7; Func<int,int> closure=x=>captured+x; Console.WriteLine(closure(5)); captured=9; Console.WriteLine(closure(5));
        Accumulator receiver=new DoubleAccumulator(); Func<int,int> bound=receiver.Apply; Console.WriteLine(bound(3));
        Action a=()=>Console.WriteLine("a"); Action b=()=>Console.WriteLine("b");
        Action combined=a+b+a; combined(); combined-=a; combined();
        Console.WriteLine(combined==a+b); Console.WriteLine(combined!=a); Console.WriteLine(a==a);
        Compute custom=Inc; Console.WriteLine(custom(9007199254740993L));
    }
}
