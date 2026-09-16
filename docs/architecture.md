# Current compiler architecture

Updated 2026-09-16 after recovery of `f87fe22` and the source-backed awaitable/async-stream implementation. Output schema 2; compiler profile `portable-mvp`; optional `portable-bcl-v1`. Earlier snapshots remain in [history](history/README.md); their status statements are historical.

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
                        instruction or basic-block source emission
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

Source binding and implementation binding remain separate. Reference assemblies cannot supply executable stubs. Identity checks permit one version per assembly simple name; complete forwarding, binding redirects, multi-load-context semantics and package restore are not implemented. Framework and portable-type normalization still use explicit policies, not a general loader.

## Storage, types and dispatch

Closed specialization retains constructed identities and per-instantiation statics within budgets. CliValue represents copied structs, CliRef storage addresses and CliBox boxed identity. Struct loads/stores/calls/returns copy; addresses remain aliases. Nullable has its own boxing rules. Dictionary/set/comparer algorithms use explicit equality/hash/order services and selected Object/ValueType virtual bridges.

Class slots distinguish overrides from newslot. Interface linkage handles the tested implicit/explicit MethodImpl and variance combinations. Delegates retain receiver/method identity. Generic constraints, default-interface/GVM combinations and byref lifetimes are not fully certified.

Target runtime layers cover the base semantic model, managed storage, host services, value semantics, binary32/RVA numeric operations, exceptions and arrays. Rectangular arrays retain lengths/lower bounds and checked row-major storage. Opaque type handles support limited identity/array-shape operations, not arbitrary member reflection.

## Control flow and source lowering

The analyzer exposes CilControlFlowGraph with explicit blocks, normal edges and conservative exceptional/leave edges. It validates instruction/prefix boundaries and selected protected-region transfer rules. DefiniteLocalAssignment proves normal-flow initialization for methods without InitLocals; exceptional/address-first initialization remains conservative.

Instruction dispatch emits a case per reachable instruction. Block dispatch coalesces straight-line source into validated blocks without removing managed checks or losing the IL offset needed for an exception. Neither mode executes a serialized IL stream. Block dispatch is not SSA, stack elimination, devirtualization or general structured-source recovery. Both remain gated against the same DLL oracle.

## Awaitable and iterator protocols

Tasks, composition, completion sources and cancellation are translated managed algorithms with a cooperative FIFO. ValueTask can contain an inline result, Task or IValueTaskSource plus token. The completion core supports reusable sequential operations and clears callback/state before user continuation. AsTask consumes a source once; a Task-backed preserved value can then be reused.

Async iterators are actual Roslyn-generated classes and method bodies. Portable IAsyncEnumerable/IAsyncEnumerator/IAsyncDisposable, AsyncIteratorMethodBuilder, completion-core and configured-enumeration implementations satisfy their dependencies. No compiler syntax shortcut or native generator substitution is used. Awaited finally/disposal and linked cancellation remain ordinary imported IL behavior.

ExceptionDispatchInfo preserves the exception object for generated cleanup paths, not .NET stack traces. Exception filters still require a separate correct cross-frame search-before-unwind protocol and are rejected. Forwarded source continuation flags do not imply context capture, threading or timer support.

Host-invoked Task/ValueTask operations are explicit roots before pruning. invokeAsync/invoke_async adapts supported results and drives the queue. Native async-generator marshalling is not delivered; an exported Task can consume a managed stream. Step budgets are not execution preemption.

## Heap boundaries and evolution

Ordinary generated objects use host GC. Weak references, identity hashes, KeepAlive and explicit root handles have declared host semantics. LogicalHeap owns only its independent payloads and explicit roots. A general logical/native collector profile requires compiler-created descriptors, roots, safepoints, interior-reference ownership and barriers.

Next compiler work is structured identity/loader policy, effect-aware managed HIR/CFG/SSA, stronger exception/byref verification and explicit host capabilities. Broad reflection/dynamic code and C++ are separate milestones. See [specification](specification.md), [async contracts](async-streams.md) and [implementation plan](implementation-plan.md).
