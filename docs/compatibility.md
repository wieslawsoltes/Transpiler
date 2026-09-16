# Current compatibility ledger

Updated 2026-09-16. JavaScript and Python share analysis and lowering. A supported slice means the named implementation paths and corpus pass, not exhaustive ECMA/.NET certification. Metadata schema 2; profile portable-mvp; optional portable-bcl-v1.

| Area | Implemented/tested slice | Remaining boundary |
|---|---|---|
| C# / PE input | Roslyn C# 14; real DLL import; Debug/Release | No general project/NuGet/source-generator driver |
| Assembly graph | Explicit multi-assembly linkage; identity mismatch rejection; deterministic input order | General forwarding, multi-version/load-context resolution |
| Framework binding | Selected reference-pack metadata, distinct implementation bodies | Full hermetic restore/lockfile pipeline |
| Generics | Bounded closed method/type specialization and distinct statics | Dynamic/open instantiation; exhaustive constraints and sharing |
| Structs / enums | Copy/address/storage/boxing paths; nested fields; tested formatting | Explicit layout, full nullable/span/ref-struct semantics |
| Interfaces | Tested implicit/explicit MethodImpl, constrained calls, variance | All default-interface/GVM combinations |
| Delegates / closures | Function/receiver identity, invocation, combination/removal/equality | Arbitrary unmanaged function pointers and complete delegate variance/interop |
| Classes / virtual slots | Constructors, fields, statics, internal overrides/newslot | Implicit external virtual-slot bridges; finalizers |
| Local initialization | Stack checks plus must-assignment proof when InitLocals is false | First initialization through addresses; exceptional-flow proof |
| Numerics | Fixed-width integers, checked conversion, exact 64-bit values, binary64 | Binary32 storage, decimal, SIMD, all formatting cases |
| Arrays | Checked SZ vectors, tested covariance and managed enumeration bridge | Rectangular/lower-bound arrays and field-RVA initializers |
| Strings | Identity/interning and selected UTF-16 operations | Full globalization/formatting and isolated-surrogate console streaming |
| Exceptions | Catch/throw/rethrow/leave/finally and pending-handler behavior | Two-pass filters; exact traces/default messages; full fault fixtures |
| Original CoreLib | 21 integer Math methods in UpstreamBclCatalog | Not the complete Math class or arbitrary CoreLib |
| Portable List<T> | Construction, Add/AddRange, index/Count/Capacity, Insert/RemoveAt/RemoveAll/Clear, ToArray, ForEach, enumeration | Sort, full comparer/equality API, all collection interfaces/overloads |
| Portable Queue<T>/Stack<T> | Selected storage, mutation, Try operations, enumeration/materialization | Full public interface/overload parity |
| Portable Enumerable | Selected Range/Repeat, Where/Select, Take/Skip, Count/Any/All/First, Aggregate/Sum, ToArray/ToList | Full LINQ, IQueryable, async LINQ and every overload |
| Iterators | Real Roslyn iterator bodies, repeated enumeration and early disposal | Every iterator/language/runtime edge case |
| Async | Task/Task<T>, builders, awaiters, Yield, selected completion-source paths, host adapters | Thread pool, Delay/Run, contexts, tokens, ValueTask, async streams |
| Host lifetime | WeakReference<T>, identity hash, KeepAlive, explicit roots | Forced collection, finalization/resurrection, pinning, CLR heap APIs |
| Logical GC library | Translated C# bounded mark/sweep; roots, weak clearing, cycles, stale-address/quota checks | Not the default collector; no implicit local roots, finalizers, compaction or concurrency |
| Reflection / dynamic | Explicit rejection; no hidden fallback | Metadata reflection, dynamic loading, expression compilation |
| I/O / native / threads | Selected console and single-thread services only | File/network/GUI/native ABI and memory-model adapters |
| Host ABI | Primitive/string and selected array input; async results; root handles | General callbacks/byref/object transport across modules/processes |
| Source generation | Deterministic static method dispatch | SSA optimizations, source maps, structured source recovery |
| Browser / C++ | Browser ES-module design; C++ design profiles only | Browser qualification and actual C++ emitters |
| Security | Input import without assembly execution; bounded selected graph structures | Full verifier, hostile-input audit, quotas and sandbox |

## Evidence dimensions

The configured gate has **71 harness cases**: 52 normal/BCL Release/Debug console configurations, eight negative fixtures, ordinary library ABI, malformed PE, and nine extended checks. Logical-heap Release/Debug cases add four translated console executions beyond the 104 ordinary/BCL console executions. Other ABI and graph cases perform additional target runs.

Every positive console configuration compares stdout and exit status to the same DLL executed by CoreCLR and checks repeated emission. The original-body gate asserts all 21 selected CoreLib algorithms remain emitted IL-derived methods, not hidden Math intrinsics. The assignment gate checks 11 hand-authored control-flow shapes. Logical collection also checks an independent graph oracle, not just agreement among three executions of the same algorithm.

The capabilities command enumerates normalized opcode names and registered fixed intrinsic signatures. Pattern-based generic/runtime bindings and valid operand combinations are separate dimensions. No percentage of full CLI or BCL support is claimed. Consult [validation](validation-summary.md) for observed results rather than inferring success from configured counts.
