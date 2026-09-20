# Testing and reproducibility

Updated 2026-09-20 for compiler input contracts, generalized stream acquisition, host clocks and the SSA/native regression matrix.

```bash
dotnet build Transpiler.slnx -c Release
python3 tests/conformance.py
```

The package-free Python harness launches dotnet, Node and Python processes with per-child timeouts, invariant oracle globalization and UTF-8 test output. Artifacts under artifacts/conformance include real assemblies, generated source, analyses, manifests, diagnostics and report.json. CI clears TRANSPILER_TEST_FILTER; a filtered local run is not a complete gate.

## Current configured topology

There are currently **274 registered harness cases**. The report independently records selected cases, observed results and whether the run is complete or filtered. Counts describe test groupings, not CLI support percentages; earlier 135-case evidence belongs to the earlier filter/forwarding milestone.

For each positive console configuration the same DLL is executed with CoreCLR and translated to both targets. Stdout and process exit status must agree, and repeated emission must be byte-identical. The 110 ordinary configurations account for 220 generated console executions; the SSA matrix exercises those fixtures separately. The block-dispatch gate adds 48 target/configuration pairs with both instruction and block output: 96 executions. Host/graph/logical-heap cases run additional programs.

Extended gates cover source-host protocol, block mode, ValueTask/cancellation cleanup, WhenAny loser cleanup, definite assignment, protected-region CFG, original-body provenance, JS liveness, logical heap Debug/Release, three-assembly linkage, portable BCL provenance, ordinary host async/roots and explicit unsupported boundaries.

## New async protocol tests

ValueTaskSources tests source status/token/result, flags, queued and inline completion, late registration, reset reentrancy, AsTask/Preserve, fault/cancellation and struct results. ValueTaskSourceEdges uses reset-on-consumption to catch duplicate result reads, distinguishes a Faulted source throwing OperationCanceledException, and checks invalid registration/completion and 16-bit token rollover.

AsyncStreams and AsyncStreamEdges test actual Roslyn iterator bodies in Debug and Release: suspended yields, early break, awaited finally/disposal, independent enumeration, covariance, generic/struct values, linked cancellation, source exception identity and replacement by disposal failure. ExceptionDispatchInfo tests preserve the object, not .NET trace formatting.

The source-host gate completes operations through a JS yieldHost callback and an asyncio task, checks one result consumption per operation, and inspects cleared callback/state fields. Its manifests require actual managed completion-core and Task-bridge bodies. Remote stack injection and ExecutionContext capture remain rejection tests.

## Existing guarantees retained

The original-body gate requires 21 real CoreLib method bodies and no Math intrinsic substitution in its fixture. Logical collection has an independent integer-graph oracle, cycle/weak/root/generation/quota checks; it is not a test of host GC timing. CompilerChecks exercises local assignment and protected-region/prefix flows. Hash-storage and collection tests cover collisions, reuse, comparer and enumeration behavior.

Instruction/block comparison requires equal outputs and fewer dispatch cases/source bytes for the selected fixtures, with deterministic block re-emission. It does not measure throughput or prove SSA optimization. JSON output exposes DispatchCaseCount alongside emitted-instruction and method counts.

## Interpretation and provenance

Read [validation](validation-summary.md) for observed runs and exact environments. Preserve complete reports, source-archive commit identity, toolchain inputs and notices together. Do not count a configured or filtered suite as passed, or normalize output to hide backend differences. An intentional profile difference needs a documented dedicated test.

Remaining qualification includes comprehensive exceptional/byref/type verification, additional native/interception filter coverage, concurrency/context and calendar/thread integration beyond host-clock-v2, nested/custom awaitable stream protocols and object transport, browser/OS/architecture matrices, memory-pressure tests without nondeterministic liveness assumptions and performance benchmarks. Compilation and generated execution remain outside any security sandbox guarantee.

## Native host-stream gate

`host_stream_batch.py` compiles a library with ordinary async iterators and instrumented IValueTaskSource probes. Debug/Release × instruction/block produces four harness cases. Each executes 22 JavaScript and 23 Python lifecycle groups, a native host value consumer compared with CoreCLR, deterministic re-emission and cursor-origin checks. The same library assembly is the input for every consumer in a configuration.

Probe counters independently track issuing and consuming move/disposal. Disposal overlapping a move or issued twice fails. Tests cover ignored cancellation, external completion, pending disposal and close retry, native abort/asyncio cancellation, empty/faulting acquisition, Current failure, single-use ownership and terminal reference/listener cleanup. Python tests both async-with and contextlib.aclosing early exit. JavaScript and Python exception-precedence expectations are tested separately, not normalized into fake equivalence.

At the earlier host-stream milestone, the full local 125-case suite passed under SDK 10.0.100, Node 22.16.0 and Python 3.13.5. Focused host-stream success is also recorded separately and is not substituted for that full result. See the validation ledger for CI/artifact identity and scope. Abandoned or forever-pending resources are not certified as reclaimable by an async finalizer.

## Filters, forwarding and verification gates

FilterOrdering and FilterAsync cover search-before-unwind across callers, library callbacks, delegates, iterators and async code. The extra filter gate exercises both dispatch modes and repeated invocation, checking activation and search-plan retirement. The old Filter negative fixture is now positive.

IdentityChecks runs 25 structural/forwarding assertions. The end-to-end facade test executes an unchanged App.dll after replacing Contracts.dll with a forwarder to Destination.dll. SafetyChecks runs 22 local-origin, indirect-width, exception-assignment and filter-layout assertions. A separate persisted assembly tests fault on exceptional exit, no fault on normal exit, filter-before-fault ordering and nonzeroed exception locals against CoreCLR.

`TRANSPILER_TEST_WORKERS` accepts one through eight workers; default one, CI two. Every registered case owns its artifact directory and reports retain registration order. No later cases are dropped after a failure. Reports include `registeredCases`, `selectedCases`, `filter`, `workers` and `complete`. An empty selection returns failure instead of a green report. A previous report is removed before starting, so interruption cannot leave an old success masquerading as the new run. These harness checks are separate from compiler/runtime parallelism.

## Host-clock regression gates

`node tests/timers/clock_unit.mjs` and `python tests/timers/clock_unit.py` execute 13 direct service checks each without requiring the compiler. `TRANSPILER_TEST_FILTER=host/timers/ python tests/conformance.py` executes the compiled lifecycle groups under instruction and SSA dispatch: 12 scenarios per target, 48 in total, with deterministic repeated emission and real CoreCLR async oracles. TimerValidation participates in both Debug/Release differential matrices. These APIs are now positive coverage; the full suite rejects duration parsing, provider calendar APIs and timer WaitHandle disposal.

The independent clock-lifecycle CI job supplies early feedback; it does not replace the unfiltered conformance job. Each test subprocess has a timeout. Runtime pump budgets themselves are not wall-clock watchdogs, and a manually controlled clock can leave a legitimate operation pending until the host advances it. See [host clocks](host-clocks.md).

## Duration/provider and periodic timer gates

`TRANSPILER_TEST_FILTER=host/time-services/ python tests/conformance.py` runs four Debug/Release × instruction/SSA groups, each with 22 scenarios on both hosted targets (176 scenario executions), repeated deterministic emission, managed-body provenance and CoreCLR oracles. `TimeValues` contributes four more duration/validation differential cases. The clock-lifecycle CI job retains its earlier timer report as clock-report.json before executing the expanded group. See [time-services.md](time-services.md).

## Generalized stream discovery and acquisition

`CompilerChecks --streams` runs 30 structural discovery/budget assertions. `TRANSPILER_TEST_FILTER=interop/stream-exports/ python tests/conformance.py` selects six Debug/Release × instruction/block/SSA cases. Each emits the same library DLL to JavaScript and Python, runs 45 lifecycle scenarios per host, compares with a CoreCLR consumer of that same DLL, checks translated Factory/Cursor method provenance and repeats byte-identical emission: 540 translated lifecycle executions in total.

The independent stream-lifecycle CI job runs these checks without replacing the complete conformance job. Cases include concrete/inherited/interface/struct shapes, Task/ValueTask/source-backed acquisition, explicit erased and ambiguous choices, upfront rejection, null/fault/cancellation/acquisition errors, factory cleanup retry, no-move close, native abort/asyncio cancellation, delayed disposal, independent factories and exact Int64 struct elements. All tests retain their process timeouts. [The contract](stream-exports.md) distinguishes pending ownership from successful retirement.

## Offline input-contract gates

`dotnet tests/CompilerChecks/bin/Release/net10.0/CompilerChecks.dll --inputs` runs 52 direct snapshot, lock, closure-policy and cancellation invariants. `TRANSPILER_TEST_FILTER=inputs/ python tests/conformance.py` runs five end-to-end groups; the full suite also registers the direct invariant group, for six additional harness cases.

The end-to-end corpus covers a real three-assembly metadata cycle on both hosted targets, byte-identical ordinary/SSA output after relocating implementations and reordering directories, explicit-reference equivalence, unchanged forwarded consumers, and C# source/BCL locked replay. Source lock fixtures pin 167 reference-pack files and seven compiler/toolchain images in the observed .NET 10 environments. Changed root/library/reference-pack/compiler fingerprints and emission options are rejected.

Negative cases cover same-identity different-content candidates, wrong versions, reference-only assemblies, corrupt matching filenames, missing dependencies, unsafe names, graph/file/byte limits and malformed/duplicate lock JSON. A failed replay preserves an existing output; diagnostics cannot overwrite an explicit or already-captured dependency. Snapshot tests distinguish immutable cached reads from new sessions and exercise pre-canceled and mid-import cancellation.

The independent input-contracts CI job runs the invariants and all five end-to-end groups without replacing the unfiltered conformance job. `samples/input-locks/build.py` separately builds a two-library application, records both target locks, relocates its assemblies and verifies identical output with result 42. See [input-locks.md](input-locks.md) for exact replay, I/O and cancellation boundaries. These tests do not certify all hostile metadata, complete environment isolation or preemptive CPU cancellation.
