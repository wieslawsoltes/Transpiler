# BCL transpilation and runtime reuse — research and implementation

Research checked 2026-09-16. The original pre-implementation research is preserved in [history](../history/0.1/bcl-runtime-planning.md). This revision separates primary-source findings, engineering recommendations, and code actually delivered.

## Executive conclusion

**Yes: translate managed BCL algorithms using the same PE/CIL compiler as application code.** Import a reviewed method/type dependency closure from an implementation assembly, or compile a portable C# implementation to IL and translate that. A reference assembly supplies API metadata, not executable algorithms: Microsoft explicitly distinguishes implementation assemblies from reference assemblies and documents placeholder bodies. [1]

**Yes: implement runtime components in managed code or integrate native components through an explicit backend contract.** CoreLib is not entirely independent of its execution engine: the official architecture describes native layout dependencies, InternalCall/FCall, QCall and P/Invoke boundaries. Translating the managed portion does not implement those boundaries by itself. [2]

**Do not conflate three different heaps.** Ordinary generated objects use their JavaScript/Python host heap. This repository now also contains a working C# logical mark/sweep heap, whose own IL is translated to both targets. A future native backend could adapt a collector such as CoreCLR GC. Neither experiment makes the other two implicit dependencies.

## 1. Implementation categories

| Origin | Example in this repository | What is translated |
|---|---|---|
| Original upstream implementation IL | 21 selected integer Math overloads | Real bodies from the selected System.Private.CoreLib implementation |
| Original portable managed implementation | List/Queue/Stack, selected LINQ, tasks/builders | Transpiler.Bcl C# compiled to ordinary IL |
| Managed runtime algorithm | LogicalHeap mark/sweep, handle tables, logical allocation | Transpiler.Runtime.Managed C# compiled and explicitly linked |
| Host-backed runtime primitive | WeakReference<T>, identity hashes, output, storage helpers | Declared JS/Python service implementation; not counted as upstream IL |
| Native or OS service | Pinning, threads, native I/O, collector integration | Not implemented by these profiles; explicit adapters remain necessary |

Original portable code is not described as unchanged upstream code. A method-origin manifest identifies the assembly supplying every emitted body and records input SHA-256 hashes. The upstream notice is embedded in generated source containing framework implementation bodies.

## 2. Original BCL adoption: now a tested vertical slice

`UpstreamBclCatalog`, policy `corelib-integer-v1`, selects these exact static signatures: BigMul(Int32,Int32); Min and Max for SByte, Byte, Int16, UInt16, Int32, UInt32, Int64 and UInt64; DivRem with an out remainder for Int32 and Int64; and Sign for Int32 and Int64. That is **21 original method bodies**, not the complete Math class.

The loader requires a real CoreLib implementation, verifies selected bodies exist and rejects missing/native/reference-only entries. The shared linker and analyzer remain responsible for the body dependency closure. A same-signature intrinsic does not supersede an imported body. The provenance test asserts all 21 appear as emitted CoreLib bodies and no Math intrinsic appears in that fixture.

This expansion found a concrete compiler dependency: original DivRem implementations omit zero-initialization of their local frame. The compiler now proves normal-flow definite assignment with an intersection fixed point over incoming paths. A local read or address acquisition requires a prior store on every incoming path. Exceptional regions and address-based first initialization remain conservatively rejected. This avoids silently changing unsupported uninitialized-memory semantics into zero initialization.

The source algorithms can be examined in the upstream Math implementation. Our emitted bodies are taken from the actual selected binary; current upstream source is research evidence, not a claim that it is byte-identical to every serviced installation. [3]

## 3. Portable managed BCL: shared algorithms, not duplicate backends

The portable library supplies selected List<T>, Queue<T>, Stack<T> and Enumerable operations. Storage, mutation, version checks and lazy iterator algorithms are C# methods translated into both languages. The compiler handles closed generic identities, struct copies, constrained calls, delegates and interface maps needed by these methods. Array/string enumeration has a host-representation bridge into a translated managed enumerator.

Tasks, awaiters and async builders are also managed C# algorithms. The compiler executes actual Roslyn-lowered Release struct and Debug class state machines. Host async adapters drive a cooperative FIFO scheduler and unwrap supported results. This is an explicit scheduling profile, not a promise of full thread-pool, SynchronizationContext, ExecutionContext, timer or cancellation-token behavior.

Use this adoption process for the next library family: exact API contract; complete reachable implementation closure; metadata and storage rules; target/runtime dependencies; differential ordinary/error/identity/copy tests; provenance and licenses. Adding names to a substitution table without these layers is not support.

## 4. Why a native GC is not another BCL assembly

The CoreCLR design and collector interfaces describe collaboration with an execution engine for heap layout, root scanning, suspension, allocation contexts, write barriers and handles. CoreCLR's native collector cannot simply be fed to a PE/CIL importer. [4,5]

The documented .NET standalone-GC facility loads a native collector library in place of the default collector. Its name does **not** mean the collector needs no execution-engine integration. A future cpp-managed backend would have to supply the corresponding runtime/GC boundary, object descriptors, roots, barriers and safepoint protocol. [5,6]

| Candidate | Evidence and trade-off | Recommendation for this project |
|---|---|---|
| JavaScript/Python host GC | Native host objects and host reference facilities already exist; JS specifies weak liveness without collection-time guarantees. [7,8] | Default for source output; declare the missing .NET observables |
| Translated logical mark/sweep | Implemented here with explicit roots and logical payload ownership | Keep as an independently testable runtime algorithm and integration reference |
| CoreCLR GC | Native collector with execution-engine interfaces. [4–6] | Candidate for a future managed native profile, not a JS/Python dependency |
| Mono SGen | Documented nursery/major collection, descriptors, barriers and runtime interfaces; this is architecture documentation, not an audited current product certification. [9] | Study integration and layout contracts; do not assume drop-in reuse |
| Boehm-Demers-Weiser | Project documents a conservative collector for C/C++. [10] | Possible restricted native experiment; conservatism does not establish exact CLI weak/finalization behavior |
| MMTk | Project provides a reusable memory-management toolkit and VM integration approach. [11] | Alternative native-runtime research direction; no binding or performance comparison is implemented here |

No performance ranking of these collectors is claimed. They were researched, not benchmarked against one another in this batch.

## 5. Host lifetime contract and the KeepAlive correction

JavaScript uses WeakRef/WeakMap; Python uses weakref/WeakKeyDictionary and weak-reference-enabled managed wrappers. Host root tables retain only explicitly retained objects, not every allocation. Release removes a root without pretending to force collection. Identity hashes are stable within a module runtime, not numerically identical to a separate CLR process.

An empty JavaScript function call is not a specified liveness primitive. ECMAScript explicitly places WeakRef construction/dereference targets on its kept-alive list for the current synchronous job. The JS KeepAlive implementation now uses that mechanism, conservatively extending lifetime to the job boundary; missing WeakRef support fails explicitly. The regression test verifies the barrier path and host-capability rejection without asserting nondeterministic collection timing. [7]

Weak references are not deterministic cleanup. The JavaScript specification allows objects to remain uncollected indefinitely and finalization callbacks to be omitted. Python's weakref and gc APIs describe Python-host behavior, not the CLR collector contract. Consequently GC.Collect, WaitForPendingFinalizers, resurrection tracking, pinning and heap-generation statistics are not supplied as misleading no-ops. [7,8,12]

## 6. The implemented logical collector

`LogicalHeap` is a separate, explicitly linked managed assembly. It owns fixed-capacity object and handle tables, byte payloads and declared reference slots. Allocation has object/byte/reference/handle budgets. Address wrappers include owner identity, slot and a monotonic generation; generation overflow is checked. A retired address cannot alias newly allocated storage in the recycled slot.

Collection runs only when explicitly requested. It resets marks, marks strong handle targets, iteratively traces declared edges, clears weak targets to unmarked nodes, and sweeps/recycles nodes. Each node is queued once, so a preallocated stack bounded by object capacity suffices. The C# collector loop does not allocate user payloads or invoke callbacks; generated method-frame helpers may still allocate host objects, so this is **not an allocation-free target-runtime performance claim**.

The graph oracle independently computes reachability over integer-indexed graphs. Tests check 20 deterministic randomized rounds, rooted/unrooted cycles, weak clearing, stale/foreign addresses, released roots, quotas, bounds and accounting. The same collector assembly is executed by CoreCLR and translated for both targets.

A HeapReference variable is deliberately **not** a logical root. Only explicit strong handles protect logical payloads. Ordinary generated class instances still use host GC. There are no logical finalizers, resurrection, compaction, generations, concurrent marking, automatic collection or compiler stack-map integration in this implementation.

## 7. Integration required before changing the default collector

To use a logical/native collector for ordinary compiled objects, add explicit object descriptors and heap allocation lowering; frame/static/exception/delegate/task/interop roots; interior-reference ownership; root lifetimes at calls and safepoints; and a verified mutation barrier protocol. Every target adapter must preserve these invariants under optimization. Moving collectors additionally need relocation or handle indirection. Generational/concurrent collectors need remembered sets, synchronization and appropriate barriers. These are compiler/runtime integration tasks, not solved by the isolated graph algorithm. [4,5,9]

Keep a restricted std-only C++ ownership profile distinct from a cpp-managed profile with runtime services. Choosing one must not silently impose the dependencies or guarantees of the other.

## 8. Provenance and acceptance

Reused dotnet/runtime implementation material carries its MIT notice; the repository also publishes separate third-party notices. Preserve applicable notices with generated derivatives and binary bundles, and review any additionally imported library separately. Existing implementation hashes identify consumed binaries; adopted source should additionally record repository revision, path and modifications. [13,14]

Acceptance is per semantic slice: exact binding, actual translated body, supported dependency closure, both targets, failure/boundary tests and inspectable provenance. Current status is in [compatibility](../compatibility.md), observed test results in [validation](../validation-summary.md), and future work in [implementation plan](../implementation-plan.md).

## Primary sources

Accessed 2026-09-16. Unpinned documentation may change; compilation manifests identify consumed implementation binaries.

1. Reference versus implementation assemblies: https://learn.microsoft.com/en-us/dotnet/standard/assembly/reference-assemblies
2. CoreLib/runtime architecture: https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/corelib.md
3. Math implementation: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Math.cs
4. CoreCLR GC design: https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/garbage-collection.md
5. Collector/execution-engine contract: https://github.com/dotnet/runtime/blob/main/src/coreclr/gc/gcinterface.h
6. Standalone collector configuration: https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#standalone-gc
7. ECMAScript weak liveness and kept objects: https://tc39.es/ecma262/#sec-processing-model-of-weakref-and-finalizationregistry-targets
8. Python weak references: https://docs.python.org/3/library/weakref.html
9. SGen architecture: https://www.mono-project.com/docs/advanced/garbage-collector/sgen/
10. Boehm-Demers-Weiser collector: https://www.hboehm.info/gc/
11. Memory Management Toolkit: https://www.mmtk.io/
12. Python collector API: https://docs.python.org/3/library/gc.html
13. dotnet/runtime license: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
14. dotnet/runtime notices: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT
