using System;
using System.Collections.Generic;
using System.Linq;
public static class Program
{
    public static void Main()
    {
        var list = new List<int>();
        for (int i=0;i<8;i++) list.Add(i*i);
        list.Insert(2,100); list.RemoveAt(0);
        Console.WriteLine(list.Count); Console.WriteLine(list[1]);
        foreach (int x in list) Console.WriteLine(x);
        var copy = list.ToArray(); Console.WriteLine(copy[3]);
        Console.WriteLine(list.RemoveAll(x=>x>20));
        list.AddRange(list); Console.WriteLine(list.Count);
        var e = list.GetEnumerator(); e.MoveNext(); list.Add(9);
        try { e.MoveNext(); } catch (InvalidOperationException) { Console.WriteLine("version checked"); }
        var q = new Queue<string>(); q.Enqueue("one");q.Enqueue("two");
        Console.WriteLine(q.Dequeue()); Console.WriteLine(q.Peek());
        foreach (var x in q) Console.WriteLine(x);
        var stack = new Stack<long>();stack.Push(9007199254740993L);stack.Push(7);
        foreach (var x in stack) Console.WriteLine(x);
        Console.WriteLine(stack.Pop());
        Console.WriteLine(Math.BigMul(int.MaxValue, int.MaxValue));
    }
}
