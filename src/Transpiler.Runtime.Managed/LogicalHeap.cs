using System;

namespace Transpiler.Runtime.Managed;

/// <summary>A logical address, not a CLR/native pointer. Keeping this wrapper alive is NOT a logical GC root.</summary>
public sealed class HeapReference
{
    internal readonly LogicalHeap Owner;
    internal readonly int Slot;
    internal readonly long Generation;
    internal HeapReference(LogicalHeap owner, int slot, long generation)
    { Owner = owner; Slot = slot; Generation = generation; }
}

/// <summary>Explicit logical root or weak handle. Release it through its owning heap.</summary>
public sealed class HeapHandle
{
    internal readonly LogicalHeap Owner;
    internal readonly int Slot;
    internal readonly bool Weak;
    internal HeapReference? Target;
    internal bool Active;
    internal HeapHandle(LogicalHeap owner, int slot, HeapReference? target, bool weak)
    { Owner = owner; Slot = slot; Target = target; Weak = weak; Active = true; }
}

public readonly struct HeapStatistics
{
    public readonly int LiveObjects;
    public readonly long PayloadBytes;
    public readonly int ReferenceSlots;
    public readonly int StrongHandles;
    public readonly int WeakHandles;
    public readonly long Collections;
    public readonly long TotalAllocations;
    internal HeapStatistics(int objects, long bytes, int references, int strong, int weak, long collections, long allocations)
    { LiveObjects = objects; PayloadBytes = bytes; ReferenceSlots = references; StrongHandles = strong;
      WeakHandles = weak; Collections = collections; TotalAllocations = allocations; }
}

/// <summary>
/// Bounded, non-moving, single-threaded mark/sweep heap implemented entirely in translatable managed C#.
/// Collect is an explicit safepoint. Only strong handles and their transitive reference fields are roots.
/// This heap is NOT installed as the collector for ordinary generated objects; it owns only its own payloads.
/// </summary>
public sealed class LogicalHeap
{
    private sealed class Node
    {
        internal readonly long Generation;
        internal readonly byte[] Bytes;
        internal readonly HeapReference?[] References;
        internal bool Marked;
        internal Node(long generation, int bytes, int references)
        { Generation = generation; Bytes = new byte[bytes]; References = new HeapReference?[references]; }
    }

    private readonly Node?[] _nodes;
    private readonly HeapHandle?[] _handles;
    private readonly int[] _freeNodes;
    private readonly int[] _freeHandles;
    private readonly int[] _markStack;
    private readonly long _maximumBytes;
    private readonly int _maximumReferences;
    private int _freeNodeCount;
    private int _freeHandleCount;
    private int _referenceSlots;
    private int _strongHandles;
    private int _weakHandles;
    private long _payloadBytes;
    private long _allocations;
    private long _collections;

    public LogicalHeap(int maximumObjects, long maximumPayloadBytes, int maximumReferenceSlots, int maximumHandles)
    {
        if (maximumObjects <= 0) throw new ArgumentOutOfRangeException(nameof(maximumObjects));
        if (maximumPayloadBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));
        if (maximumReferenceSlots < 0) throw new ArgumentOutOfRangeException(nameof(maximumReferenceSlots));
        if (maximumHandles <= 0) throw new ArgumentOutOfRangeException(nameof(maximumHandles));
        _nodes = new Node?[maximumObjects]; _handles = new HeapHandle?[maximumHandles];
        _freeNodes = new int[maximumObjects]; _freeHandles = new int[maximumHandles]; _markStack = new int[maximumObjects];
        _freeNodeCount = maximumObjects; _freeHandleCount = maximumHandles;
        _maximumBytes = maximumPayloadBytes; _maximumReferences = maximumReferenceSlots;
        for (int i = 0; i < maximumObjects; i++) _freeNodes[i] = maximumObjects - 1 - i;
        for (int i = 0; i < maximumHandles; i++) _freeHandles[i] = maximumHandles - 1 - i;
    }

    public HeapReference Allocate(int byteCount, int referenceCount)
    {
        if (byteCount < 0) throw new ArgumentOutOfRangeException(nameof(byteCount));
        if (referenceCount < 0) throw new ArgumentOutOfRangeException(nameof(referenceCount));
        if (_freeNodeCount == 0 || byteCount > _maximumBytes - _payloadBytes || referenceCount > _maximumReferences - _referenceSlots)
            throw new InvalidOperationException("Logical heap capacity exceeded; collect at an explicit safepoint or increase the budget.");
        long generation = checked(_allocations + 1); // Never silently wrap and alias a retired address.
        int index = _freeNodes[_freeNodeCount - 1];
        // Allocate all host representations before publishing or consuming a free-list entry.
        var node = new Node(generation, byteCount, referenceCount);
        var address = new HeapReference(this, index, generation);
        _freeNodeCount--; _nodes[index] = node; _allocations = generation;
        _payloadBytes += byteCount; _referenceSlots += referenceCount;
        return address;
    }

    private Node Resolve(HeapReference? address)
    {
        if (address == null) throw new ArgumentNullException(nameof(address));
        if (!object.ReferenceEquals(address.Owner, this)) throw new ArgumentException("Address belongs to a different logical heap.");
        Node? node = _nodes[address.Slot];
        if (node == null || node.Generation != address.Generation) throw new InvalidOperationException("Logical address was collected or replaced.");
        return node;
    }

    public bool IsAlive(HeapReference? address)
    {
        if (address == null || !object.ReferenceEquals(address.Owner, this)) return false;
        Node? node = _nodes[address.Slot];
        return node != null && node.Generation == address.Generation;
    }

    public byte ReadByte(HeapReference address, int offset)
    {
        Node node = Resolve(address);
        if ((uint)offset >= (uint)node.Bytes.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        return node.Bytes[offset];
    }
    public void WriteByte(HeapReference address, int offset, byte value)
    {
        Node node = Resolve(address);
        if ((uint)offset >= (uint)node.Bytes.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        node.Bytes[offset] = value;
    }
    public HeapReference? ReadReference(HeapReference address, int offset)
    {
        Node node = Resolve(address);
        if ((uint)offset >= (uint)node.References.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        return node.References[offset];
    }
    public void WriteReference(HeapReference address, int offset, HeapReference? target)
    {
        Node node = Resolve(address);
        if ((uint)offset >= (uint)node.References.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (target != null) Resolve(target); // All edge stores validate ownership and generation before mutation.
        node.References[offset] = target;
    }

    public HeapHandle Retain(HeapReference? target) => CreateHandle(target, false);
    public HeapHandle CreateWeak(HeapReference? target) => CreateHandle(target, true);
    private HeapHandle CreateHandle(HeapReference? target, bool weak)
    {
        if (target != null) Resolve(target);
        if (_freeHandleCount == 0) throw new InvalidOperationException("Logical handle capacity exceeded.");
        int slot = _freeHandles[_freeHandleCount - 1];
        var handle = new HeapHandle(this, slot, target, weak);
        _freeHandleCount--; _handles[slot] = handle;
        if (weak) _weakHandles++; else _strongHandles++;
        return handle;
    }
    private void ValidateHandle(HeapHandle handle)
    {
        if (handle == null) throw new ArgumentNullException(nameof(handle));
        if (!object.ReferenceEquals(handle.Owner, this)) throw new ArgumentException("Handle belongs to a different logical heap.");
        if (!handle.Active || !object.ReferenceEquals(_handles[handle.Slot], handle)) throw new InvalidOperationException("Logical handle was released.");
    }
    public HeapReference? GetTarget(HeapHandle handle) { ValidateHandle(handle); return handle.Target; }
    public void SetTarget(HeapHandle handle, HeapReference? target)
    { ValidateHandle(handle); if (target != null) Resolve(target); handle.Target = target; }
    public bool Release(HeapHandle handle)
    {
        if (handle == null) throw new ArgumentNullException(nameof(handle));
        if (!object.ReferenceEquals(handle.Owner, this)) throw new ArgumentException("Handle belongs to a different logical heap.");
        if (!handle.Active) return false;
        ValidateHandle(handle);
        handle.Active = false; handle.Target = null; _handles[handle.Slot] = null;
        _freeHandles[_freeHandleCount++] = handle.Slot;
        if (handle.Weak) _weakHandles--; else _strongHandles--;
        return true;
    }

    private void Mark(HeapReference address, ref int count)
    {
        Node node = Resolve(address);
        if (node.Marked) return;
        node.Marked = true; _markStack[count++] = address.Slot;
    }

    /// <summary>Collects only this logical heap. No host-GC calls, callbacks, finalizers, recursion or allocations occur.</summary>
    public int Collect()
    {
        long collections = checked(_collections + 1);
        for (int i = 0; i < _nodes.Length; i++) if (_nodes[i] != null) _nodes[i]!.Marked = false;
        int count = 0;
        for (int i = 0; i < _handles.Length; i++)
        {
            HeapHandle? handle = _handles[i];
            if (handle != null && !handle.Weak && handle.Target != null) Mark(handle.Target, ref count);
        }
        while (count != 0)
        {
            Node node = _nodes[_markStack[--count]]!;
            for (int i = 0; i < node.References.Length; i++)
                if (node.References[i] != null) Mark(node.References[i]!, ref count);
        }
        // Clear weak targets before recycling storage; old wrappers may remain reachable in the host.
        for (int i = 0; i < _handles.Length; i++)
        {
            HeapHandle? handle = _handles[i];
            if (handle != null && handle.Weak && handle.Target != null && !Resolve(handle.Target).Marked) handle.Target = null;
        }
        int reclaimed = 0;
        for (int i = 0; i < _nodes.Length; i++)
        {
            Node? node = _nodes[i];
            if (node == null || node.Marked) continue;
            _payloadBytes -= node.Bytes.Length; _referenceSlots -= node.References.Length;
            _nodes[i] = null; _freeNodes[_freeNodeCount++] = i; reclaimed++;
        }
        _collections = collections;
        return reclaimed;
    }

    public HeapStatistics GetStatistics() => new HeapStatistics(_nodes.Length - _freeNodeCount, _payloadBytes,
        _referenceSlots, _strongHandles, _weakHandles, _collections, _allocations);
}
