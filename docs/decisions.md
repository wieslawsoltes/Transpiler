# Architecture decisions

Updated 2026-09-16. Historical initial decisions are preserved in [0.1](history/0.1/decisions.md).

## ADR-001 — Real PE/CIL remains canonical: accepted

Roslyn supplies C# compilation; the shared engine imports the actual emitted assembly. Existing DLL input must not require reconstructed source. Source-specific recognition can improve optimization/provenance but cannot be the only correctness path.

## ADR-002 — Library origin is explicit: accepted

Distinguish original implementation IL, original portable managed algorithms and target runtime primitives. Reference assemblies supply contracts only. Manifests identify inputs and emitted bodies. Exact unsupported members fail; no same-name or hidden CLR fallback is allowed.

## ADR-003 — Closed-world specialization is bounded: accepted for current profile

Preserve constructed identities, generic statics and storage semantics. Add explicit host roots before pruning methods called from adapters. Reject expansion/open-instantiation requirements beyond the profile rather than silently erasing types. General sharing, dynamic loading and complete constraints remain separate work.

## ADR-004 — Verify nonzeroed locals rather than forbid or approximate them: accepted

Normal-flow must-assignment permits useful real BCL bodies without accepting uninitialized reads. Intersect predecessor facts, including loop-entry paths. Reject unproven address and exceptional-flow initialization. Keep this targeted proof separate from claims of full ECMA verification.

## ADR-005 — Conservative source backend remains an oracle: accepted

Static control-flow dispatch handles general branches without runtime IL decoding. Preserve it when adding HIR/CFG/SSA and target fast paths. Performance/source-idiomaticity are not evidence of semantic correctness.

## ADR-006 — Default host heap and logical collector stay separate: accepted

Use JS/Python objects for ordinary generated storage. The transpilable C# LogicalHeap is an explicit algorithm library with its own payload/root contract, not an installed replacement for all managed allocations. Compiler stack maps, safepoints, barriers and interior roots are prerequisites for a broader collector profile.

## ADR-007 — Liveness must use a defined host mechanism: accepted

An empty JavaScript helper is not a specified KeepAlive barrier. Use the ECMAScript kept-alive mechanism and reject unavailable host support. Do not expose forced collection, finalization, resurrection or pinning as no-op implementations.

## ADR-008 — Cooperative async is a declared scheduling profile: accepted

Execute actual Roslyn state machines against translated managed builders and a FIFO scheduler. Promise/asyncio adapters drive that queue; they do not establish thread-pool/context/timer equivalence. Unsupported scheduling APIs remain diagnostics. Pump-step budgets do not replace process execution limits.

## ADR-009 — Exception filters require search-before-unwind: accepted

Do not approximate cross-frame filters with predicates after the host stack has unwound. Retain explicit rejection until a two-pass managed-frame protocol and region verification are implemented.

## ADR-010 — Native source profiles have different runtime obligations: proposed

Keep a restricted standard-library ownership C++ profile distinct from managed C++ output with collector/runtime services. Reusing a native collector requires its execution-engine contract, not merely linking a library. Neither C++ target is implemented here.
