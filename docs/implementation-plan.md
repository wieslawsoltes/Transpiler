# Updated implementation plan

Updated 2026-09-16. The [initial full milestone design](history/0.1/implementation-plan.md) is preserved. This plan records delivered slices and the remaining dependency-ordered work; it does not mark entire CLI feature families complete because a representative library now executes.

## Delivered in the current work batch

Explicit multi-assembly linking and SDK reference-pack binding; bounded closed generics and separate static storage; struct copies and managed storage references; tested interfaces/MethodImpl/variance and delegates; portable C# collections/LINQ/iterators; cooperative C# tasks/awaiters/builders and host async ABI; host weak references/roots/identity services; provenance and upstream notices; 21 selected original CoreLib integer methods; definite local assignment; and an explicitly linked, transpilable logical mark/sweep heap.

These establish an end-to-end library path. Full forwarding/constraints/value layouts, broad BCL, full scheduling and ordinary-object GC replacement remain open. The compatibility ledger and test reports qualify each slice.

## Priority 1 — Make library adoption systematic

Create structured, scope-preserving type/member identities rather than extending string substitution indefinitely. Add reviewed facade/type-forwarding resolution, exact method-origin policies, selected implementation-closure discovery, package/reference locks and user-configurable dependency roots. Preserve the existing exact failure behavior when no implementation is available.

Grow original CoreLib adoption through small reviewed catalogs: integer helpers first, then scalar bit/encoding/string algorithms with explicit runtime boundaries. Each method must have real emitted code, a pinned input hash, both-target tests and rejected adjacent unsupported APIs. Do not execute reference stubs or let intrinsic bindings silently mask missing imported code.

Acceptance: reproducible multi-package graph with deliberate identity/forwarding conflicts; replay from a manifest; reports identify every body, contract and runtime dependency. Add source/PDB provenance and cancellation/resource budgets to the public compiler API.

## Priority 2 — Complete collection-enabling semantic gaps

Implement equality/comparer contracts, Object/ValueType external virtual bridges, nullable boxing and richer generic constraints. Use these foundations for Dictionary<TKey,TValue>, HashSet<T>, complete List collection interfaces and additional LINQ operations. Implement field-RVA initializers and binary32 storage rules as independently gated slices.

Acceptance: custom comparers, collision-heavy dictionaries, struct keys/values, mutation during enumeration, default/null inputs, recursive generics, boxing and delegate callbacks agree with the selected .NET contract. Full interface/GVM/default-method tests must accompany changes to dispatch, not only successful collection samples.

Suggested commits: identity and comparer contracts; external virtual bridge; primitive/value equality; dictionary storage; adversarial comparer corpus; set/collection interfaces; RVA/numeric gates; updated surface ledger.

## Priority 3 — Scheduling and host capabilities

Keep current cooperative FIFO explicit. Introduce host capabilities for timers, completion notification, cancellation and event-loop integration, then implement selected Task combinators and cancellation-token semantics. Define context/reentrancy policies before promising .NET scheduling equivalence. Add ValueTask/async streams only with their required lifetime/interface contracts.

Acceptance: synchronous and suspended completion, cancellation versus fault, callback reentrancy, timer ordering, disposal during suspension, fairness and multiple concurrent host invocations are tested. Missing host capabilities fail instead of running blocking or synchronous approximations. A pump-step budget is not a substitute for an execution watchdog.

## Priority 4 — Managed CFG, effects and verification

Introduce explicit normal/exceptional basic-block edges and storage/address/value operations. Extend definite-assignment proof through exception regions and address initialization. Validate protected-region transfers and byref lifetime/escape rules. Preserve throwing/allocation/type-initialization/callback/suspension effects before stack-to-SSA and optimizer passes.

Acceptance: hand-authored malformed IL fails before emission; filters are implemented only with a correct two-pass cross-frame search protocol. Optimized source must match the baseline dispatcher and CoreCLR on deterministic and randomized corpora. Benchmark compilation time, output size, startup, allocations and hot-path execution separately from compatibility.

## Priority 5 — Logical heap integration, only as a separate profile

The current logical collector is a working algorithm library, not the ordinary application's GC. Before integration, add compiler-created descriptors, explicit root frames, static/exception/task/delegate/interop roots, interior-address owners and safepoint liveness. Keep ownership/generation/quota invariants testable independently.

Acceptance: every live managed root survives collection at every injected safepoint; root removal makes unreachable cycles collectible; local/interior references survive reassignment and exceptions; no runtime bookkeeping accidentally roots all allocations. Keep allocation and collector bootstrap storage separate. Finalization/resurrection, relocation, generational barriers and concurrency require additional contracts and tests; they are not inferred from a passing mark/sweep graph test.

## Longer-range targets

Metadata reflection and trimming roots precede an opt-in dynamic-code profile. Browser/Windows/macOS/ARM64 matrices precede deployment claims. A std-only C++ ownership subset and cpp-managed collector-backed output remain separate planned targets. Research CoreCLR GC, SGen, Boehm and MMTk as execution-engine integration alternatives without treating any as an already integrated dependency.

## Completion rule

A feature slice needs specification references, importer/type representation, verification, linkage/reachability, runtime/library behavior on each claimed target, positive/error/identity/copy tests, capability diagnostics and documentation/provenance. Existing partial support must remain visible. Add a complete vertical slice before increasing the advertised surface.
