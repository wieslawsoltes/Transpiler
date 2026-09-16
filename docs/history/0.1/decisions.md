# Architecture decision records

## ADR-001: PE/CIL is the canonical compatibility input — accepted

Use Roslyn to produce a real assembly, then import PE metadata and CIL. This allows existing DLL input and prevents duplicated C# syntax handling in every backend. A future source-semantic sidecar may improve diagnostics/optimization, but must not become necessary for correctness.

Alternative rejected as the sole engine: direct C# syntax substitution. It does not cover arbitrary compiler-produced assemblies and tempts each backend to reimplement source lowering independently.

## ADR-002: Runtime services are explicit — accepted

Standalone means no original CLR process or dynamically downloaded managed runtime at application execution. It does not mean that integer-width semantics, boxing, managed references, string identity, or exceptions disappear. Bundle small target-language implementations and expose their profile limitations.

Alternative rejected: silent CLR/Python.NET/Wasm fallback. An explicit future hosted profile can be useful but must be a separate deliverable.

## ADR-003: Conservative source dispatch before optimization — accepted for MVP

Emit statically known method bodies using control-flow dispatchers and explicit evaluation stacks. This handles general branch shapes and provides a readable correctness baseline without pretending an SSA optimizer already exists.

Consequence: generated source is larger and slower than a mature optimized compiler may achieve. Preserve the baseline when adding block coalescing, SSA, structured source, or fast numeric lowering.

## ADR-004: Capability rejection is a product behavior — accepted

Unknown reachable opcodes, unsupported types, unlinked methods, and known implicit runtime obligations must fail before new target output. Track exact library signatures, not method-name guesses. Some import shapes are conservatively rejected even when unused.

Consequence: compiling a modern C# source successfully with Roslyn does not guarantee its emitted library/runtime dependencies fit the profile. This is preferable to silently incorrect generated programs.

## ADR-005: Exception filters need a two-pass protocol — accepted

Do not substitute a host catch predicate after the stack has already unwound. Preserve the CLI search-before-unwind model through future managed frame metadata/shadow-stack support. Reject filters until that mechanism exists.

## ADR-006: C++ has separate native and managed profiles — proposed

Plan a restricted `native-std` target and a broader `cpp-managed` target. Standard-library-only dependencies are not equivalent to absence of managed runtime services. Heap cycles, interior references, weak references, finalization, and type identity must have an explicit policy.

## ADR-007: Test one assembly against two targets and an oracle — accepted

Compile each fixture once per source optimization mode, execute it with CoreCLR, and compile those same bytes to JavaScript and Python. Compare output/exit and re-emission determinism. Include rejection gates and host ABI checks. A green suite is measured evidence for the corpus, not a universal conformance certificate.
