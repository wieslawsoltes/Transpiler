# BCL and runtime reuse research — 2026-09-16

## Conclusions

Yes: managed BCL implementations can be inputs to the same CIL compiler as application code. The useful unit of adoption is a **reachable method/type closure**, not an entire framework DLL treated as an indivisible capability. Reference assemblies provide the compile-time API contract; implementation assemblies provide executable IL. They must never be confused: a reference assembly can contain `throw null` placeholder bodies and is not an implementation. [1]

Yes: runtime facilities can be reimplemented in target languages or integrated through a separately declared native backend. That includes allocation, type metadata, boxed values, managed references, exception dispatch, handles, scheduling, and collection. However, the existing CoreCLR GC is native code with an execution-engine interface; it is not another managed BCL assembly that the IL compiler can simply translate. [2–4]

For JavaScript/Python, use host garbage collection for ordinary host-backed managed objects. Add explicit target-neutral contracts for roots/handles, allocation, type layouts, copies, weak references, finalization policy and safepoints. Do not promise that host weak-reference/finalizer timing or Python's `gc.collect()` is equivalent to the .NET GC API. For a future explicit managed heap, roots and heap references must be visible to that collector; wrapping host objects without tracking live frames does not accomplish this. [3–6]

## 1. Four implementation categories

| Category | Examples | Policy |
|---|---|---|
| Ordinary managed code | Comparisons, loops, collection algorithms, many string/number helpers | Compile original implementation IL where its dependency closure is supported |
| Managed code with runtime assumptions | CoreLib object/type helpers, constrained generics, array allocation, reflection | Compile the managed portion and bind explicit runtime primitives |
| Native runtime services | GC, thread suspension, allocation fast paths, QCalls, native interop | Implement target service contracts or integrate a declared native runtime |
| Host/OS services | Files, networking, timers, GUI, synchronization | Require an explicit host adapter and capability declaration |

CoreLib is tightly coupled to the execution engine. Its source includes shared managed implementations as well as runtime-specific pieces. The .NET repository separates runtime, libraries, and shared host sources. A pure algorithm and an InternalCall/QCall boundary are different engineering tasks even when both appear under a `System.*` API. [2,7,8]

## 2. Two complementary BCL adoption paths

**Original implementation IL.** Select a small allowlist of real implementation methods, include their reachable helpers, and pass them through the ordinary linker/specializer/analyzer/emitter. Start with simple integer Math.Min/Max overloads; verify against the same .NET implementation. Record the implementation assembly's identity and content hash. An intrinsic with the same public signature must not silently win over the imported managed body.

**Portable C# implementations.** Put shared managed implementations in a separate C# library, compile it once to IL, and translate that IL into both target languages. Use explicit, signature-validated replacement maps from framework types/members to that library. A portable List<T> or builder is not represented by separate handwritten JavaScript and Python container algorithms. Its algorithmic method bodies should be visible in emitted code and the linked-method manifest.

Do not label a portable rewrite as unchanged upstream BCL code. Track origin as `upstream-il`, `adapted-source`, `original-portable`, or `runtime-intrinsic`. Include the exact supported member surface; implementing Add/Count/indexing does not imply all List<T> interfaces, constructors, serialization, overloads, or edge behavior.

## 3. Compiler dependencies unlocked by real libraries

Multi-assembly linkage needs deterministic module/token remapping and identity validation. Closed generic types need distinct constructed identities and static storage. Structs need explicit value copies and storage addresses, including references into a struct that is later reassigned. Interface calls need declarations, implicit/explicit implementation maps, and correct value-type receivers. Delegates need captured receiver identity, invocation lists, and call targets. Async/iterators require state-machine and scheduling/disposal contracts, not only source pattern recognition.

These are dependencies of BCL compilation, not optional polishing. A compiler that substitutes host objects for structs can appear correct in a simple List<int> example while corrupting List<Struct> copying, boxing, or enumerator mutation behavior.

Reference-only interface descriptors may be used as contracts if all executable calls are resolved to real implementations or explicit primitives. This is different from executing a reference assembly's placeholder body. The loader must reject reference assemblies supplied as implementation dependencies. [1]

## 4. GC options and trade-offs

### Host-managed object profile

Ordinary generated objects, arrays, strings, boxes, delegates, tasks and references remain host objects. Host GC follows their host references, including captured storage references. This avoids imposing a second tracing heap on every JavaScript/Python program. It does not provide CLR object addresses, pinning, exact generations, finalization resurrection, heap walking or the .NET GC tuning API.

A handle table can provide stable opaque handles and strong roots. Handles must be released explicitly and detect stale/double-release misuse. Weak handles need a host feature plus a declared semantic profile. A strong root table accidentally used for every allocated object would leak the entire managed heap.

### Explicit logical managed heap

A separate mark/sweep reference implementation can be written in C# and transpiled to both targets. It can validate allocation, graph traversal, root registration, cycle collection, weak-handle clearing and safepoint interfaces. An explicit logical heap manages only the objects/slots allocated through its API; it is not automatically the collector for ordinary generated objects. That integration additionally needs compiler-emitted stack roots, static roots, exception/delegate/task roots, interior-reference ownership, and host-bridge roots.

Start with non-moving single-threaded collection and explicit safepoints. Moving collection adds reference relocation or handle indirection. Generational/incremental/concurrent collection adds remembered sets, barriers, tri-color invariants, synchronization, and pause/resume requirements. Exact byte layout and unmanaged interop belong to a separately qualified memory profile. [3,4]

### Reusing CoreCLR GC

The GC interfaces expose the collaboration between the collector and the execution engine, including root scanning, suspension, allocation contexts, object/type metadata, handles, and write barriers. A C++ backend could adapt these contracts and reuse appropriate upstream code, subject to license/notice obligations and runtime integration work. JavaScript/Python source output cannot treat that native subsystem as ordinary CIL. A Wasm/native bridge is a different deployment profile and must be declared as such. [3,4]

### Lifetime is not disposal

Deterministic resource cleanup should use Dispose/using or explicit host handles. JavaScript finalization and weak-reference behavior must not be used as a substitute for required resource release. Python's collector interface controls its host collector, not a reimplementation of all .NET GC observables. [5,6]

## 5. Licensing and provenance

The dotnet/runtime repository publishes an MIT license and a separate third-party-notices file. Reused source/binaries/generated derivatives need the relevant notices preserved; a repository-wide MIT label must not erase individual third-party obligations. Pin source revisions or record exact implementation hashes. For source extraction, record upstream path, commit, local adaptation and license. Original portable implementations should be identified as original, not attributed to upstream. [9,10]

The initial batch should not import the entire native runtime or vendor an unreviewed source subtree. Prove a small method slice and a small portable library, then expand the ledger with actual test evidence.

## 6. Acceptance criteria

An adopted managed BCL operation is accepted only when generated code contains its translated method body, its dependency closure is declared, both backends pass differential tests, and unsupported adjacent overloads fail explicitly. Test null/error paths, integer boundaries, struct copying, mutation during enumeration, explicit interface calls and delegate identity—not just successful output for one sample.

A runtime service must identify whether it is host-backed, a translated managed implementation, or native. GC tests must distinguish logical-heap collection from ordinary host-object reclamation. Do not report GC conformance based solely on a mark/sweep demo that the application compiler never uses.

## Sources

Accessed 2026-09-16. Current branch documentation can change; implementation manifests should pin the actual consumed inputs.

1. Microsoft, reference versus implementation assemblies: https://learn.microsoft.com/en-us/dotnet/standard/assembly/reference-assemblies
2. .NET CoreLib architecture: https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/corelib.md
3. .NET GC design: https://github.com/dotnet/runtime/blob/main/docs/design/coreclr/botr/garbage-collection.md
4. Collector/execution-engine interfaces: https://github.com/dotnet/runtime/blob/main/src/coreclr/gc/gcinterface.h
5. ECMAScript weak-reference/finalization specification: https://tc39.es/ecma262/multipage/managing-memory.html
6. Python GC interface: https://docs.python.org/3/library/gc.html
7. .NET runtime/libraries repository: https://github.com/dotnet/runtime
8. Shared Math implementation: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Math.cs
9. .NET runtime MIT license: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
10. .NET runtime third-party notices: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT
