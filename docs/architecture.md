# Architecture — managed compiler, translated libraries and explicit runtime services

Updated 2026-09-16. Output metadata schema 2; compiler profile `portable-mvp`; optional library substitution policy `portable-bcl-v1`; reviewed original-body catalog `corelib-integer-v1`. These are implemented subsets, not universal CLI or complete .NET compatibility.

The detailed preceding design is archived in [0.2](history/0.2/architecture.md), and the initial design in [0.1](history/0.1/architecture.md). This page is the current architecture and supersedes their implementation-status statements. See [research](research/bcl-runtime-2026-09-16.md), [specification](specification.md) and [compatibility](compatibility.md).

## Implemented pipeline

```text
C# source -> Roslyn + selected .NET reference pack -> real PE/CIL
Existing root DLL -------------------------------> metadata/CIL importer
Explicit implementation DLLs --------------------> deterministic assembly linker
Optional abstract interface contracts ----------> declaration-only metadata
Portable C# BCL assembly -------------------------> explicit substitution policy
Selected original CoreLib bodies ----------------> same linker and compiler
                                                      |
                                      bounded closed-generic specialization
                                                      |
                              reachability + stack states + definite local assignment
                                                      |
                                  exact capability checks + class/interface linkage
                                                      |
                                     JavaScript/Python source method emission
                                                      |
                                     target helpers + explicit host services
```

The canonical input is actual managed executable metadata and method bodies, not reconstructed C# syntax. Existing DLLs bypass source compilation but use the same semantics. Generated modules contain statically emitted methods, not their original DLL or a runtime instruction stream. The baseline backend uses source-level control-flow dispatch and evaluation stacks; CFG/SSA optimization and structured source recovery remain future work.

## Physical modules

| Module | Implemented role |
|---|---|
| Transpiler.Core | Import, scoped identities, explicit assembly linkage, substitutions, closed specialization, stack/local analysis and capability contracts |
| Transpiler.Frontend.Roslyn | C# compilation, reference-pack inputs, PortableCompilation orchestration and reviewed upstream-body catalog |
| Transpiler.Bcl | Original portable C# collection, LINQ/iterator, task, awaiter and builder algorithms |
| Transpiler.Runtime.Managed | Independently linked logical-heap allocation/mark/sweep/handle algorithms in C# |
| Transpiler.Backends | Shared source emission, linkage metadata, value/reference semantics, JS/Python helpers and host adapters |
| Transpiler.Cli | Explicit inputs, profile options, diagnostics, analysis and method-origin manifests |
| CompilerChecks / Python harness | Hand-authored control-flow checks, CoreCLR differential execution, ABI/provenance/rejection/graph gates |

Neither managed-library project depends on source-printing/compiler implementation classes. The logical heap is not automatically linked or installed as the application's collector.

## Binding and provenance

Source compilation selects .NET 10 reference-pack contracts, not the compiler process's trusted platform assembly list. Implementation dependencies are explicit. Scoped application type names and requested assembly version/culture/public-key-token identity are preserved within a one-version-per-simple-name linking profile. Input ordering does not change output. This is not complete type-forwarding, binding-redirect, multi-load-context or MSBuild/NuGet resolution.

Reference assemblies cannot supply executable dependency bodies. The portable loader imports a narrow abstract-interface metadata set as contracts only. Its implementation assembly contains portable algorithms. LibrarySubstitution maps explicit portable type definitions to reviewed framework identities, while exact method signatures continue to constrain resolution.

UpstreamBclCatalog imports 21 original integer Math bodies from real CoreLib. Required definitions must exist and contain managed IL. Imported bodies win over otherwise available runtime intrinsics; their reachable dependencies still undergo normal capability analysis. Manifests distinguish emitted assembly bodies from external bindings and retain input hashes. Upstream-generated derivatives include the .NET notice.

## Specialization, storage and dispatch

Closed generic specialization substitutes type and method contexts, preserves constructed type identity and separates static storage. It has explicit expansion budgets. Host-invoked array enumerators and async-adapter methods become roots before pruning; ordinary direct-call reachability alone would miss them.

CliValue models copied struct values. CliRef models storage locations. Struct load/store/argument/return/boxing paths copy, while field addresses retain parent storage identity even when the containing struct is reassigned. Enum storage and tested formatting are separate from reference-type identity.

Class slots distinguish overrides from newslot. Interface linkage handles tested implicit/explicit MethodImpl and variance cases. Constrained receivers preserve mutable value storage. Delegates carry translated method/receiver identity and immutable invocation sequences; closure bodies remain ordinary translated methods. Full generic constraints, all interface/default-method combinations, arbitrary external virtual bridges and unmanaged calli are not certified.

## Verification boundary

Typed stack states are propagated to a fixed point and joins must match. Methods without InitLocals now have a separate must-be-assigned analysis: intersect local sets at normal-flow joins, then reject reads/address acquisition lacking a store on every path. Exception-region initialization and address-based first writes are deliberately outside that proof. Backend profile guards also reject implicit finalization and unsupported external overrides.

This is still not a complete ECMA verifier or security boundary. Exceptional region-transfer rules, byref lifetimes, object subtype proofs and hostile-metadata/resource hardening need additional work.

## Async and exceptions

Portable tasks/builders/awaiters and the FIFO scheduler are managed C# methods. Actual Roslyn Release/Debug state machines execute through normal IL lowering. JavaScript invokeAsync and Python invoke_async adapt linked task results and cooperatively drive the queue, with a bounded number of pump steps. The budget does not interrupt an infinite managed call or cancel an underlying task.

The scheduling profile has no thread pool, wall-clock timers, SynchronizationContext/ExecutionContext or full cancellation-token semantics. Task.Run/Delay remain rejected. Non-filter exception handling uses explicit handler/finally continuations. Cross-frame two-pass filter search remains a distinct unimplemented subsystem.

## Two implemented heap boundaries, one future native boundary

**Default host heap:** managed wrappers are JS/Python objects. HostedRuntime supplies explicit strong roots, weak references, identity hashes and output/async integration. JS KeepAlive uses the ECMAScript kept-alive mechanism, not an empty helper. Runtime information reports unsupported forced collection, managed finalizers and pinning. Root release is not deterministic destruction.

**Optional logical heap:** Transpiler.Runtime.Managed owns logical byte/reference payloads and bounded object/handle tables. Its C# collector traces explicit strong roots, clears weak targets, sweeps cycles and checks allocation generations. Host-local HeapReference wrappers are not logical roots. The tested collector is separate from ordinary generated objects; [its contract](logical-heap.md) describes all invariants and limitations.

**Future native heap:** a cpp-managed backend could implement/adapt collector execution-engine contracts. It would need descriptors, barriers, frame/static/exception/task/interop roots and safepoints. A restricted std-only C++ ownership profile must remain separate. Neither C++ backend is delivered here.

## Evolution

Keep the reference dispatcher as an oracle while introducing explicit managed HIR/CFG, exception edges, storage/value separation and effects for throw/allocation/initialization/mutation/callback/suspension. Prioritize library-enabling semantics and measured optimizations over additional shallow target printers. The updated [implementation plan](implementation-plan.md) states remaining milestones and acceptance gates.
