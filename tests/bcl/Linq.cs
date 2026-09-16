using System;
using System.Collections.Generic;
using System.Linq;
public static class Program
{
    static int Disposed;
    static IEnumerable<int> Input()
    { try { for(int i=0;i<8;i++) yield return i; } finally { Disposed++; } }
    public static void Main()
    {
        var values = Enumerable.Range(3,12).Where(x => x%2 != 0).Select(x => x*x).Skip(1).Take(3);
        Console.WriteLine(values.Sum()); Console.WriteLine(values.Count());
        foreach(var x in values) Console.WriteLine(x);
        var arr = new int[4]; for(int i=0;i<arr.Length;i++) arr[i]=i+1;
        Console.WriteLine(arr.Select(x => (long)x*9007199254740993L).Sum());
        Console.WriteLine(arr.Any(x=>x==3)); Console.WriteLine(arr.All(x=>x>0));
        Console.WriteLine(arr.Aggregate(1,(a,x)=>a*x)); Console.WriteLine(arr.First());
        Console.WriteLine(Enumerable.Repeat("word",3).ToArray().Length);
        var list=values.ToList(); Console.WriteLine(list[1]);
        Console.WriteLine(Input().Take(2).Sum());Console.WriteLine(Disposed);
        Console.WriteLine(Input().Take(0).Count());Console.WriteLine(Disposed);
        try { Enumerable.Range(1,0).First(); } catch(InvalidOperationException) {Console.WriteLine("empty checked");}
        try { Enumerable.Range(int.MaxValue,2); } catch(ArgumentOutOfRangeException) {Console.WriteLine("range checked");}
        try { Enumerable.Where<int>(null, x=>true); } catch(ArgumentNullException) {Console.WriteLine("null checked eagerly");}
    }
}
