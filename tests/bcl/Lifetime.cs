using System;
using System.Runtime.CompilerServices;
public class Node { public Node Next; public int Value; }
public static class Program
{
    public static void Main()
    {
        var node = new Node { Value=42 };node.Next=node;
        var weak = new WeakReference<Node>(node);
        Console.WriteLine(weak.TryGetTarget(out var target));Console.WriteLine(target.Value);
        Console.WriteLine(object.ReferenceEquals(node,target));
        int hash=RuntimeHelpers.GetHashCode(node);
        Console.WriteLine(hash==RuntimeHelpers.GetHashCode(target));
        weak.SetTarget(null);Console.WriteLine(weak.TryGetTarget(out target));Console.WriteLine(target==null);
        weak.SetTarget(node);Console.WriteLine(weak.TryGetTarget(out target));Console.WriteLine(target.Next==node);
        GC.KeepAlive(node);
        object boxed=123;var b=new WeakReference<object>(boxed);Console.WriteLine(b.TryGetTarget(out var value));Console.WriteLine((int)value);GC.KeepAlive(boxed);
        Console.WriteLine(RuntimeHelpers.GetHashCode(null)==0);
    }
}
