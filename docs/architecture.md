# Current compiler architecture

Updated 2026-09-17 after the host-clock and timed-cancellation continuation. Output schema 2; compiler profile `portable-mvp`; optional `portable-bcl-v1`. Earlier snapshots remain in [history](history/README.md); their status statements are historical.

## Pipeline and implementation origins

```text
C# -- Roslyn / selected reference pack --> real PE + CIL + metadata
existing root DLL ----------------------> importer
explicit implementation DLLs ----------> deterministic single-load-context linker
selected abstract contracts -----------> declaration-only metadata
portable BCL and reviewed CoreLib IL ---> explicit library binding
                                            |
                                  bounded closed specialization
                                            |
                        reachability + stack states + definite assignment
                                            |
                      protected-region CFG + exact capability validation
                                            |
                         class/interface/value/host-root linkage metadata
                                            |
                        instruction / block / bounded-SSA source emission
                                      /             \
                            JavaScript helpers    Python helpers
```

Roslyn is a frontend, not the canonical intermediate representation. Existing DLLs do not need reconstructed C# source. Portable library algorithms and selected original CoreLib bodies go through the same importer and compiler. Host runtime primitives remain explicitly distinguished from translated methods in provenance manifests.

## Module responsibilities

| Project | Responsibility |
|---|---|
| Transpiler.Core | Import/identity/linking, substitutions, bounded specialization, stack/local/CFG checks and capability contracts |
| Transpiler.Frontend.Roslyn | C# compilation, reference-pack discovery, PortableCompilation and reviewed original-body catalog |
| Transpiler.Bcl | Independent C# library algorithms, including collections, tasks, cancellation and async stream protocols |
| Transpiler.Runtime.Managed | Separately linked logical-heap algorithms; not the default collector |
| Transpiler.Backends | Source generation, metadata, dispatch and layered target-runtime semantics |
| Transpiler.Cli | Explicit inputs/options, diagnostics, manifests and target output |

Source binding and implementation binding remain separate. Reference assemblies cannot supply executable stubs. Identity checks permit one version per assembly simple name; explicit scoped forwarding chains are resolved before specialization, while general framework-facade normalization, binding redirects, multi-load-context semantics and package restore remain incomplete. CliTypeIdentity models the existing type codec structurally; it is not full lossless CLI signature import. Framework and portable-type normalization still use explicit policies, not a general loader.

## Storage, types and dispatch

Closed specialization retains constructed identities and per-instantiation statics within budgets. CliValue represents copied structs, CliRef storage addresses and CliBox boxed identity. Struct loads/stores/calls/returns copy; addresses remain aliases. Nullable has its own boxing rules. Dictionary/set/comparer algorithms use explicit equality/hash/order services and selected Object/ValueType virtual bridges.

Class slots distinguish overrides from newslot. Interface linkage handles the tested implicit/explicit MethodImpl and variance combinations. Delegates retain receiver/method identity. Generic constraints, default-interface/GVM combinations and byref lifetimes are not fully certified.

Target runtime layers cover the base semantic model, managed storage, host services, value semantics, binary32/RVA numeric operations, exceptions and arrays. Rectangular arrays retain lengths/lower bounds and checked row-major storage. Opaque type handles support limited identity/array-shape operations, not arbitrary member reflection.

## Control flow and source lowering

The analyzer exposes CilControlFlowGraph with explicit blocks, normal edges and conservative exceptional/leave edges. It validates instruction/prefix boundaries and selected protected-region transfer rules. DefiniteLocalAssignment propagates must-assignment facts through normal flow and conservative pre-instruction exception entries for methods without InitLocals. Address-first writes and assignments relying only on finally/filter continuations remain conservative. ByReferenceSafety tracks returned-address origins and validates indirect storage widths/types; it is not a complete scoped-ref verifier.

Instruction dispatch emits a case per reachable instruction. Block dispatch coalesces straight-line source into validated blocks without removing managed checks or losing the IL offset needed for an exception. Neither mode executes a serialized IL stream. Block dispatch is not SSA, stack elimination, devirtualization or general structured-source recovery. Both remain gated against the same DLL oracle.

## Awaitable and iterator protocols

Tasks, composition, completion sources and cancellation are translated managed algorithms with a cooperative FIFO. ValueTask can contain an inline result, Task or IValueTaskSource plus token. The completion core supports reusable sequential operations and clears callback/state before user continuation. AsTask consumes a source once; a Task-backed preserved value can then be reused.

Async iterators are actual Roslyn-generated classes and method bodies. Portable IAsyncEnumerable/IAsyncEnumerator/IAsyncDisposable, AsyncIteratorMethodBuilder, completion-core and configured-enumeration implementations satisfy their dependencies. No compiler syntax shortcut or native generator substitution is used. Awaited finally/disposal and linked cancellation remain ordinary imported IL behavior.

ExceptionDispatchInfo preserves the exception object for generated cleanup paths, not .NET stack traces. Modules containing filters now use live-frame two-pass search before unwind. Filter entry points share arguments/locals while using separate evaluation stacks and temporary operands. Filter helper failures, initializer interception boundaries, replacement exceptions and frame retirement are tested. The persisted-IL fixture also exercises fault clauses. Native exception/stack-trace fidelity is not claimed. See [linking and verification](linking-verification.md). Forwarded source continuation flags do not imply context capture or threading. Timer support is a separate host-clock-v2 capability, not a consequence of those flags.

The managed Task.Delay/CTS implementation owns subscriptions and HostTimer handles. Seven exact internal clock signatures (including monotonic Now, with Int64 delay parameters) cross the host boundary; native JS/asyncio callbacks only publish readiness. Scheduler.RunOne executes the managed callback. Broadcast wake generations prevent concurrent host waiters from missing completion when another waiter consumes the last continuation. Blocking generated main/Wait/Result do not run the native event loop; see [host clocks](host-clocks.md).

Host-invoked Task/ValueTask operations are explicit roots before pruning. invokeAsync/invoke_async adapts supported results and drives the queue. Native async-iterator adapters are now delivered for declared IAsyncEnumerable<T> exports. A closed managed StreamCursor<T> owns the enumerator, CTS and pending move/disposal; explicit host roots retain its methods. StreamMetadata exposes the versioned method table and fails incomplete linkage with TR2220. The JS/Python protocol layer owns loop integration, cancellation notifications and result unwrapping. Cleanup timeout retains the exact operation for retry, not a hidden second enumeration. See [host streams](host-streams.md). Step budgets are not execution preemption.

## Heap boundaries and evolution

Ordinary generated objects use host GC. Weak references, identity hashes, KeepAlive and explicit root handles have declared host semantics. LogicalHeap owns only its independent payloads and explicit roots. A general logical/native collector profile requires compiler-created descriptors, roots, safepoints, interior-reference ownership and barriers.

Next compiler work includes lossless signature/loader fidelity, broader effect-aware optimization beyond the implemented stack SSA, stronger exception/byref verification and additional host capabilities. Broad reflection/dynamic code and a managed-object C++ runtime remain separate milestones from the implemented restricted scalar native-std backend. See [specification](specification.md), [async contracts](async-streams.md) and [implementation plan](implementation-plan.md).

TimeSpan, TimeProvider, public Timer, timed-wait coordinators and PeriodicTimer are original translated C# algorithms. The time-services layer uses removable input observations, timer registrations and versioned source-backed tick waits. It does not replace the reference emitter, host GC or the no-context policy. [Contract](time-services.md).
