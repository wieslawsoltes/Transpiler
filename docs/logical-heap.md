# Logical heap: executable managed runtime experiment

Implemented 2026-09-16 in `src/Transpiler.Runtime.Managed`. This is an explicitly linked library, not a new default memory profile. Ordinary generated objects retain the host-GC representation.

## Use and translate

```bash
dotnet build Transpiler.slnx -c Release
CLI=src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll
HEAP=src/Transpiler.Runtime.Managed/bin/Release/net10.0/Transpiler.Runtime.Managed.dll

dotnet "$CLI" compile tests/runtime/LogicalHeap.cs --reference "$HEAP" \
  --bcl portable --target js --out artifacts/heap.mjs --manifest artifacts/heap.json
node artifacts/heap.mjs

dotnet "$CLI" compile tests/runtime/LogicalHeap.cs --reference "$HEAP" \
  --bcl portable --target py --out artifacts/heap.py
python3 artifacts/heap.py
```

The manifest contains emitted methods from `Transpiler.Runtime.Managed`, including Collect and Mark. No target-specific collector implementation is substituted for those algorithms.

```csharp
using Transpiler.Runtime.Managed;

var heap = new LogicalHeap(128, 8192, 512, 128);
var a = heap.Allocate(byteCount: 8, referenceCount: 1);
var b = heap.Allocate(byteCount: 16, referenceCount: 1);
heap.WriteReference(a, 0, b);
heap.WriteReference(b, 0, a);
var root = heap.Retain(a);
var weak = heap.CreateWeak(b);

int first = heap.Collect();       // 0: root -> a -> b -> a
heap.Release(root);
int second = heap.Collect();      // 2: the cycle is no longer rooted
bool cleared = heap.GetTarget(weak) is null; // true
heap.Release(weak);
```

Keeping `a` or `b` in a source/host local does not retain its logical payload. A HeapReference is an address wrapper, not an automatic root. This distinction is deliberately exercised by the tests.

## Storage and invariants

Objects own byte payloads and a separate vector of logical references. These vectors are not exposed as mutable host arrays. Read/write operations validate owner identity, allocation generation and bounds. Cross-heap and stale stores fail before mutating the object graph.

Slots are recycled through preallocated free lists. Each allocation receives a monotonic checked Int64 generation; address validation compares it with the current occupant. Host address wrappers may survive a collection without keeping a recycled logical allocation valid. Handles are separate objects with owner/slot/active validation; a released handle is not reactivated when its table slot is reused.

Retain creates a strong handle, CreateWeak a weak one, and SetTarget can redirect either to a live same-heap allocation or null. GetTarget requires an active handle. Release returns true once and false thereafter; stale target access throws. Handle tables are bounded independently of object capacity.

## Collection protocol

At the explicit single-threaded safepoint: clear marks; trace strong handles and transitive reference fields; clear weak targets to unmarked nodes; sweep unmarked nodes and return slots to the free list. Marking is iterative. Each allocation is marked once and the preallocated work stack cannot exceed object capacity.

Complexity is O(object capacity + handle capacity + reachable reference slots). Allocation initializes requested payload/reference storage and consumes free-list entries only after creating its representations. Logical quota failure leaves published accounting unchanged. Actual host out-of-memory failures are not simulated or guaranteed recoverable.

Collection counters and allocation generations use checked increments. Payload/reference accounting describes this heap only, not .NET or host memory usage. Collect has no user callbacks, finalizers or allocation safepoints. Its C# algorithm does not allocate payloads during collection, but the baseline translated execution machinery still allocates host call-frame/evaluation-stack objects.

## Evidence

The fixture includes two-object cycles, root release, weak clearing, cross-heap access, stale-generation checks after reuse, capacity/bounds checks, null roots and an independent integer-graph reachability oracle. Twenty randomized rounds allocate 48 graph nodes each. The primary heap performs 963 allocations; smaller auxiliary heaps test boundaries. Both Release and Debug root assemblies are run with CoreCLR and translated to both targets, including deterministic re-emission.

This establishes the tested logical graph contract. It does not establish ordinary host-object reclamation timing, a CLR-compatible GC API, automatic source-local rooting, native layout, compiler safepoints, compaction, generational/concurrent collection, finalization or resurrection.

## Next integration boundary

Before ordinary generated objects use this collector, the compiler must emit allocation descriptors and frame/static/exception/delegate/task/interop roots; track interior-address ownership; and establish safepoints with correct liveness. Optimizations must preserve these effects. Keep this experiment independently testable until that integration has its own differential and stress gates.
