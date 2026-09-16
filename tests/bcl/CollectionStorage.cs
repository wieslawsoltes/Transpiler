using System;
using System.Collections;
using System.Collections.Generic;
public struct Pair { public int X; public int Y; public Pair(int x,int y){X=x;Y=y;} }
public static class Program
{
    public static void Main()
    {
        var list = new List<Pair>(); var p=new Pair(1,2); list.Add(p); p.X=10;
        var copy=list[0];copy.X=20;Console.WriteLine(list[0].X);
        list[0]=copy;Console.WriteLine(list[0].X);
        IEnumerator<Pair> e=((IEnumerable<Pair>)list).GetEnumerator();e.MoveNext();
        var current=e.Current;current.Y=30;Console.WriteLine(e.Current.Y);e.Dispose();
        var queue=new Queue<int>(2);for(int i=0;i<80;i++){queue.Enqueue(i);if(i%3==0)Console.WriteLine(queue.Dequeue());}
        foreach(var x in queue) Console.WriteLine(x);
        queue.Clear();Console.WriteLine(queue.TryDequeue(out var empty));Console.WriteLine(empty);
        var stack=new Stack<int>();for(int i=0;i<16;i++)stack.Push(i);
        var en=stack.GetEnumerator();en.MoveNext();stack.Pop();try{en.MoveNext();}catch(InvalidOperationException){Console.WriteLine("stack version");}
        stack.Clear();Console.WriteLine(stack.TryPop(out empty));
        try{list[100]=p;}catch(ArgumentOutOfRangeException){Console.WriteLine("list bounds");}
        try{new List<int>(-1);}catch(ArgumentOutOfRangeException){Console.WriteLine("capacity checked");}
        var le=list.GetEnumerator(); IEnumerator ie=le;
        try{var value=ie.Current;}catch(InvalidOperationException){Console.WriteLine("enumerator state");}
        IEnumerable array = new int[3]; foreach(object item in array) Console.WriteLine(item);
        IEnumerable text = "AB"; foreach(object item in text) Console.WriteLine(item);
        list.ForEach(x=>Console.WriteLine(x.X));
        try{list.ForEach(x=>list.Add(x));}catch(InvalidOperationException){Console.WriteLine("foreach version");}
    }
}
