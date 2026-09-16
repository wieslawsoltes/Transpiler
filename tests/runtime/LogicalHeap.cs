using System;
using Transpiler.Runtime.Managed;
public static class Program
{
    static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Heap invariant failed."); }
    public static void Main()
    {
        var heap = new LogicalHeap(128, 8192, 512, 128);
        var a = heap.Allocate(8, 1); var b = heap.Allocate(16, 1);
        heap.WriteReference(a, 0, b); heap.WriteReference(b, 0, a);
        heap.WriteByte(b, 15, 255);
        var root = heap.Retain(a); var weak = heap.CreateWeak(b);
        Check(heap.Collect() == 0 && heap.IsAlive(b) && heap.ReadByte(b, 15) == 255);
        Check(object.ReferenceEquals(heap.ReadReference(a, 0), b));
        Check(heap.Release(root) && !heap.Release(root));
        Check(heap.Collect() == 2 && !heap.IsAlive(a) && heap.GetTarget(weak) == null);
        var replacement = heap.Allocate(0, 0);
        Check(!heap.IsAlive(a) && !heap.IsAlive(b) && heap.IsAlive(replacement));
        try { heap.ReadByte(b, 0); throw new Exception("accepted stale address"); }
        catch (InvalidOperationException) { Console.WriteLine("generation checked"); }
        try { heap.GetTarget(root); throw new Exception("accepted stale root"); }
        catch (InvalidOperationException) { Console.WriteLine("released root checked"); }
        var other = new LogicalHeap(2, 32, 2, 2);
        try { heap.Retain(other.Allocate(1, 0)); throw new Exception("accepted foreign address"); }
        catch (ArgumentException) { Console.WriteLine("ownership checked"); }
        heap.Release(weak); Check(heap.Collect() == 1);
        // Deterministic graph-oracle stress: independent transitive closure over integer indices.
        uint random = 0x12345678;
        for (int round = 0; round < 20; round++)
        {
            var nodes = new HeapReference[48]; var roots = new HeapHandle[8];
            var weakNodes = new HeapHandle[48]; var edges = new int[96]; var marked = new bool[48];
            for (int i = 0; i < 48; i++) { nodes[i] = heap.Allocate(3, 2); weakNodes[i] = heap.CreateWeak(nodes[i]); }
            for (int i = 0; i < 96; i++)
            { random = unchecked(random * 1664525 + 1013904223); edges[i] = (int)(random % 48); heap.WriteReference(nodes[i / 2], i % 2, nodes[edges[i]]); }
            for (int i = 0; i < 8; i++) { roots[i] = heap.Retain(nodes[i * 6]); marked[i * 6] = true; }
            for (int pass = 0; pass < 48; pass++)
                for (int i = 0; i < 48; i++) if (marked[i]) { marked[edges[i * 2]] = true; marked[edges[i * 2 + 1]] = true; }
            int alive = 0; for (int i = 0; i < 48; i++) if (marked[i]) alive++;
            Check(heap.Collect() == 48 - alive);
            for (int i = 0; i < 48; i++) Check(heap.IsAlive(nodes[i]) == marked[i] && (heap.GetTarget(weakNodes[i]) != null) == marked[i]);
            var stats = heap.GetStatistics(); Check(stats.LiveObjects == alive && stats.PayloadBytes == alive * 3 && stats.ReferenceSlots == alive * 2);
            for (int i = 0; i < 8; i++) heap.Release(roots[i]);
            Check(heap.Collect() == alive);
            for (int i = 0; i < 48; i++) { Check(heap.GetTarget(weakNodes[i]) == null); heap.Release(weakNodes[i]); }
        }
        var small = new LogicalHeap(1, 1, 0, 1); var x = small.Allocate(1, 0); var h = small.Retain(x);
        try { small.Allocate(0, 0); throw new Exception("accepted overflow"); }
        catch (InvalidOperationException) { Console.WriteLine("quota checked"); }
        Check(small.GetStatistics().TotalAllocations == 1 && small.Collect() == 0);
        small.Release(h); Check(small.Collect() == 1);
        var y = small.Allocate(1, 0); Check(!small.IsAlive(x));
        try { small.WriteByte(y, 1, 3); throw new Exception("accepted bounds"); }
        catch (ArgumentOutOfRangeException) { Console.WriteLine("bounds checked"); }
        var emptyRoot = small.Retain(null); Check(small.Collect() == 1 && small.GetTarget(emptyRoot) == null);
        small.Release(emptyRoot);
        var final = heap.GetStatistics();
        Check(final.LiveObjects == 0 && final.PayloadBytes == 0 && final.ReferenceSlots == 0 && final.StrongHandles == 0 && final.WeakHandles == 0);
        Console.WriteLine(final.TotalAllocations); Console.WriteLine("logical heap verified");
    }
}
