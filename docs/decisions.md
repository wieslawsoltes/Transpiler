# Architecture decisions

Updated 2026-09-17. Historical initial decisions are preserved in [0.1](history/0.1/decisions.md).

## ADR-001 — Real PE/CIL remains canonical: accepted

Roslyn supplies C# compilation; the shared engine imports the actual emitted assembly. Existing DLL input must not require reconstructed source. Source-specific recognition can improve optimization/provenance but cannot be the only correctness path.

## ADR-002 — Library origin is explicit: accepted

Distinguish original implementation IL, original portable managed algorithms and target runtime primitives. Reference assemblies supply contracts only. Manifests identify inputs and emitted bodies. Exact unsupported members fail; no same-name or hidden CLR fallback is allowed.

## ADR-003 — Closed-world specialization is bounded: accepted for current profile

Preserve constructed identities, generic statics and storage semantics. Add explicit host roots before pruning methods called from adapters. Reject expansion/open-instantiation requirements beyond the profile rather than silently erasing types. General sharing, dynamic loading and complete constraints remain separate work.

## ADR-004 — Verify nonzeroed locals rather than forbid or approximate them: accepted

Normal-flow must-assignment permits useful real BCL bodies without accepting uninitialized reads. Intersect predecessor facts, including loop-entry paths. Use conservative pre-instruction handler/filter facts for exceptional flow; reject unproven address-first and cleanup-dependent initialization. Keep this targeted proof separate from claims of full ECMA verification.

## ADR-005 — Conservative source backend remains an oracle: accepted

Static control-flow dispatch handles general branches without runtime IL decoding. Preserve it when adding HIR/CFG/SSA and target fast paths. Performance/source-idiomaticity are not evidence of semantic correctness.

## ADR-006 — Default host heap and logical collector stay separate: accepted

Use JS/Python objects for ordinary generated storage. The transpilable C# LogicalHeap is an explicit algorithm library with its own payload/root contract, not an installed replacement for all managed allocations. Compiler stack maps, safepoints, barriers and interior roots are prerequisites for a broader collector profile.

## ADR-007 — Liveness must use a defined host mechanism: accepted

An empty JavaScript helper is not a specified KeepAlive barrier. Use the ECMAScript kept-alive mechanism and reject unavailable host support. Do not expose forced collection, finalization, resurrection or pinning as no-op implementations.

## ADR-008 — Cooperative async is a declared scheduling profile: accepted

Execute actual Roslyn state machines against translated managed builders and a FIFO scheduler. Promise/asyncio adapters drive that queue; they do not establish thread-pool/context/timer equivalence. Unsupported scheduling APIs remain diagnostics. Pump-step budgets do not replace process execution limits.

## ADR-009 — Exception filters require search-before-unwind: accepted

Do not approximate cross-frame filters with predicates after the host stack has unwound. Implemented through live managed frames and a reentrant filter entry point sharing locals/arguments but not operand stacks. Region verification rejects invalid filter layout, and CoreCLR differential tests cover search ordering, helper errors, initializer boundaries and fault unwinding. Native trace parity is not implied.

## ADR-010 — Native source profiles have different runtime obligations: proposed

Keep a restricted standard-library ownership C++ profile distinct from managed C++ output with collector/runtime services. Reusing a native collector requires its execution-engine contract, not merely linking a library. Neither C++ target is implemented here.

## ADR-011 — Async streams are managed protocol implementations: accepted

Implement IValueTaskSource, the reusable completion core, builder, configured enumeration and disposal in portable C#. Execute the real Roslyn-generated method bodies through the common pipeline. Do not replace iterator source with native host generator syntax. Native async-generator marshalling is a separate ABI feature.

## ADR-012 — Single consumption and trace/context boundaries are explicit: accepted

Source-backed values require token-aware single consumption; AsTask/Preserve provides repeatable Task-backed use. Clear callback state before invoking reentrant user code and test reset-on-consumption. Forward source flags without claiming execution-context capture. ExceptionDispatchInfo preserves the managed exception object only; exact .NET trace/remote-stack behavior remains unsupported.

## ADR-013 — Structural codec migration is not full signature fidelity: accepted

Centralize recursive scope-aware rewriting and explicit forwarder resolution now, while documenting legacy importer/specializer boundaries. Do not describe the codec model as a lossless CLI signature or multi-context loader implementation. Preserve exact substitution boundaries and record used forwarding paths.

## ADR-014 — Host lifetime is not valid CLI byref provenance: accepted

Reject returned addresses potentially tied to this method's frame or unknown storage, including aliases forwarded through opaque byref-returning calls. Host closures extending a storage cell's lifetime cannot justify an invalid managed escape. Keep this conservative proof distinct from full scoped-ref and interprocedural verification.
