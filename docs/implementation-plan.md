# Remaining implementation plan

Updated 2026-09-17. This ledger replaces obsolete initial-MVP exclusions. Completed slices remain qualified by [compatibility](compatibility.md), not by broad feature-family names.

## Delivered foundation

Real PE/CIL input and Roslyn frontend; explicit assembly linking/reference contracts; bounded generics; structs/nullable and managed references; tested interfaces/delegates/object bridges; collections/comparers/LINQ; original CoreLib slices; binary32/RVA/rectangular arrays and limited type handles; task composition/cancellation; source-backed ValueTask and async iterator/disposal protocols; protected-region CFG/block emission; host lifetime services and a separate translated logical collector; bounded stack SSA, restricted scalar C++ output, and host-clock-v1 delayed tasks/timed cancellation.

Native JS/Python async-iterator adapters are integrated. The latest continuation additionally implements two-pass managed filters, structural type rewriting, scoped forwarding and conservative byref/exception-local verification. It does not complete threading/contexts, arbitrary serialization or all BCL APIs. The existing instruction emitter remains a correctness oracle.

## 1. Typed identity, resolution and provenance

Structural rewriting of the current type codec and explicit scoped forwarding are delivered. Next migrate all importer/specializer consumers to lossless type/member/signature nodes, including optional modifiers, calling conventions and function pointers. Expand reviewed framework-facade resolution, input locks, explicit library closure discovery, cancellation and resource limits. Preserve exact diagnostics instead of introducing fallback resolution.

Acceptance: manifest-replayable multi-package graph with overload/modifier/forwarding conflicts, deterministic output, no reference-stub execution and both-target differential coverage. Add portable-PDB source provenance and source maps separately from semantic identity.

## 2. Verification and managed IR

Conservative exception-entry local assignment, returned-address origins and typed indirect accesses are delivered. Next add precise exceptional continuation/address-first initialization summaries, interprocedural scoped-ref lifetime/escape, subtype/constructor state, generic constraints and unsupported metadata validation. The bounded stack-SSA path is implemented with explicit exclusions. Extend its storage/value/effect model before broader optimization: throwing, initialization, allocation, mutation, callbacks and suspension cannot be reordered blindly.

Acceptance: adversarial hand-authored IL and randomized CFG/type tests, instruction/block equivalence and actionable failures. The managed-frame two-pass filter protocol is now implemented and tested before unwind. Expand adversarial and cross-runtime interception coverage without substituting catch predicates after host unwinding. Exact native trace/exception compatibility remains separate.

## 3. Async host capability expansion

Keep the single-thread/no-context profile explicit. The exact-interface native JS/Python async-iterator adapters are delivered with single-consumption, cancellation, scoped ownership and resumable cleanup. Next expand concrete/Task-wrapped export discovery and result marshalling only with explicit ownership/identity rules. The implemented host-clock-v1 adapter now backs Int32 Task.Delay, timed CTS construction and CancelAfter with queued-reset history, notification-only native callbacks, idle waiting and cancellation/disposal cleanup. Remaining timer scope includes TimeSpan/TimeProvider, public/periodic timers, timed waits and external I/O completion adapters; synchronous main/Wait/Result do not pump native event loops. Add context flow and concurrency only under separate implemented policies.

Acceptance: cancellation before/during move, host early return/exception, disposal failure, source reuse, simultaneous host operations and callback cleanup. Respect ValueTask single-consumption and short-token rollover. A step budget is not a watchdog. No concurrent ManualResetValueTaskSourceCore contract is claimed until synchronization is implemented and tested.

## 4. Broader managed libraries and scalars

Expand exact collection/LINQ overloads and interfaces, strings/encoding/globalization, decimal, span/ref-struct and native-layout policy. Grow original upstream-body catalogs only with complete supported closures, input hashes, licenses and both-target error/identity/copy tests. Avoid claims of framework capacity/allocation/performance parity for independent implementations.

Acceptance: custom/pathological comparers, nullable/value/reference mixtures, versioned enumerators, formatting/culture boundaries and large inputs. Type identity is not member reflection: design retained metadata/invocation and trimming roots before dynamic loading or emit.

## 5. Collector and native targets

LogicalHeap is an executable managed algorithm, not the ordinary-object collector. Integration needs descriptors, frame/static/exception/task/delegate/interop roots, safepoints, interior owners and barriers. Add independent injected-safepoint graph tests before switching allocations.

The restricted native-std scalar C++ backend is implemented. Extending it to managed-object ownership and providing a cpp-managed runtime-backed backend remain distinct projects. Native collectors require execution-engine integration; importing a managed library is not native GC reuse. Relocation, finalization, resurrection, generations and concurrent marking are separate milestones.

## 6. Optimization and deployment

Bounded stack SSA and stack elimination are implemented while retaining the reference emitter. Expand effect-aware optimization only with differential coverage and explicit fallback/rejection boundaries. Measure dispatch, compile time, allocation, source size, startup and throughput independently. Do not infer speedup from reduced case counts alone.

Qualify Windows/macOS/ARM64/browser hosts and explicit capability adapters. Add hostile-metadata fuzzing, process isolation, build/source/license manifests and versioned ABI migration tests. Completion requires tests for both claimed targets, adjacent rejection cases and current documentation, not only a successful sample.
